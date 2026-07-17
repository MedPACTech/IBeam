using System.Reflection;
using IBeam.Identity.Authorization;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using IdentityOperationAttribute = IBeam.Identity.Authorization.IBeamOperationAttribute;
using ServiceOperationAttribute = IBeam.Services.Abstractions.IBeamOperationAttribute;

namespace IBeam.Identity.Services.Authorization;

public sealed class OperationCatalogProvider : IIBeamOperationCatalogProvider
{
    private readonly IOptionsMonitor<IBeamAccessControlOptions> _options;
    private readonly Lazy<IReadOnlyList<OperationMethodDescriptor>> _attributeDescriptors;

    public OperationCatalogProvider(IOptionsMonitor<IBeamAccessControlOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _attributeDescriptors = new Lazy<IReadOnlyList<OperationMethodDescriptor>>(BuildAttributeDescriptors, isThreadSafe: true);
    }

    public Task<IReadOnlyList<AccessOperationCatalogItem>> GetOperationsAsync(Guid tenantId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var operations = new List<AccessOperationCatalogItem>();
        var resources = _options.CurrentValue.Resources
            .Where(x => !string.IsNullOrWhiteSpace(x.ResourceKey) && !string.IsNullOrWhiteSpace(x.PermissionPrefix))
            .ToList();

        foreach (var descriptor in _attributeDescriptors.Value)
        {
            operations.AddRange(BuildExplicitOperations(descriptor));
            operations.AddRange(BuildTemplateOperations(descriptor, resources));
        }

        var deduped = operations
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderBy(SourceOrder).Last())
            .OrderBy(x => x.ModuleKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<AccessOperationCatalogItem>>(deduped);
    }

    private static IReadOnlyList<AccessOperationCatalogItem> BuildExplicitOperations(OperationMethodDescriptor descriptor)
    {
        var operations = new List<AccessOperationCatalogItem>();
        var resource = descriptor.ResourceAccesses.FirstOrDefault();

        foreach (var operation in descriptor.Operations)
        {
            operations.Add(new AccessOperationCatalogItem(
                Key: operation.Key.Trim(),
                Label: FirstNonEmpty(operation.Label, HumanizePermission(operation.Key)),
                Description: NormalizeOptional(operation.Description),
                ModuleKey: FirstNonEmpty(operation.Module, null),
                ResourceType: FirstNonEmpty(operation.ResourceType, resource?.ResourceType),
                RequiredAccessLevel: FirstNonEmpty(operation.RequiredAccessLevel, resource?.AccessLevel),
                Category: FirstNonEmpty(operation.Category, CategoryFromKey(operation.Key)),
                IsAssignable: operation.IsAssignable,
                IsDangerous: operation.IsDangerous,
                Source: "attribute",
                DeclaringType: descriptor.DeclaringType,
                MethodName: descriptor.MethodName,
                IdParameter: resource?.IdParameter));
        }

        foreach (var operation in descriptor.ServiceOperations)
        {
            var key = FirstNonEmpty(operation.PermissionName, operation.Name);
            operations.Add(new AccessOperationCatalogItem(
                Key: key.Trim(),
                Label: HumanizePermission(key),
                Description: null,
                ModuleKey: CategoryFromKey(key),
                ResourceType: resource?.ResourceType,
                RequiredAccessLevel: resource?.AccessLevel,
                Category: CategoryFromKey(key),
                IsAssignable: operation.Permission,
                IsDangerous: false,
                Source: "services-attribute",
                DeclaringType: descriptor.DeclaringType,
                MethodName: descriptor.MethodName,
                IdParameter: resource?.IdParameter));
        }

        foreach (var permission in descriptor.Permissions)
        {
            operations.Add(new AccessOperationCatalogItem(
                Key: permission.Key.Trim(),
                Label: FirstNonEmpty(permission.Label, HumanizePermission(permission.Key)),
                Description: NormalizeOptional(permission.Description),
                ModuleKey: null,
                ResourceType: resource?.ResourceType,
                RequiredAccessLevel: resource?.AccessLevel,
                Category: FirstNonEmpty(permission.Category, CategoryFromKey(permission.Key)),
                IsAssignable: permission.IsAssignable,
                IsDangerous: permission.IsDangerous,
                Source: "attribute",
                DeclaringType: descriptor.DeclaringType,
                MethodName: descriptor.MethodName,
                IdParameter: resource?.IdParameter));
        }

        return operations;
    }

    private static IReadOnlyList<AccessOperationCatalogItem> BuildTemplateOperations(
        OperationMethodDescriptor descriptor,
        IReadOnlyList<AccessResourceDefinition> resources)
    {
        if (descriptor.OperationTemplates.Count == 0)
            return Array.Empty<AccessOperationCatalogItem>();

        var operations = new List<AccessOperationCatalogItem>();
        foreach (var template in descriptor.OperationTemplates)
        {
            foreach (var resource in MatchingResources(descriptor, resources))
            {
                var operation = FirstNonEmpty(template.Operation, InferOperationName(descriptor.MethodName));
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["resourceKey"] = resource.ResourceKey.Trim(),
                    ["permissionPrefix"] = resource.PermissionPrefix.Trim(),
                    ["module"] = resource.Module ?? string.Empty,
                    ["operation"] = operation,
                    ["accessLevel"] = AccessLevels.Manage,
                    ["entityName"] = resource.ClrType?.Name ?? resource.Label ?? resource.ResourceKey
                };

                var key = ResolveTemplate(template.Template, values);
                if (string.IsNullOrWhiteSpace(key) || key.Contains('{', StringComparison.Ordinal))
                    continue;

                var resourceAccess = descriptor.ResourceAccessTemplates.FirstOrDefault();
                var resourceType = resourceAccess is null
                    ? resource.ResourceKey
                    : ResolveTemplate(resourceAccess.ResourceTypeTemplate, values);
                var accessLevel = resourceAccess?.AccessLevel ?? AccessLevels.Manage;

                operations.Add(new AccessOperationCatalogItem(
                    Key: key,
                    Label: ResolveTemplate(template.LabelTemplate, values) ?? HumanizePermission(key),
                    Description: ResolveTemplate(template.DescriptionTemplate, values),
                    ModuleKey: resource.Module,
                    ResourceType: resourceType,
                    RequiredAccessLevel: accessLevel,
                    Category: FirstNonEmpty(template.Category, resource.PermissionPrefix),
                    IsAssignable: template.IsAssignable,
                    IsDangerous: template.IsDangerous,
                    Source: "attribute:template",
                    DeclaringType: descriptor.DeclaringType,
                    MethodName: descriptor.MethodName,
                    IdParameter: resourceAccess?.IdParameter));
            }
        }

        return operations;
    }

    private static IReadOnlyList<AccessResourceDefinition> MatchingResources(
        OperationMethodDescriptor descriptor,
        IReadOnlyList<AccessResourceDefinition> resources)
    {
        if (!descriptor.GenericArgumentTypes.Any())
            return resources;

        var matches = resources
            .Where(x => x.ClrType is not null && descriptor.GenericArgumentTypes.Any(g => g == x.ClrType || g.IsAssignableFrom(x.ClrType)))
            .ToList();

        return matches.Count == 0 ? resources : matches;
    }

    private static IReadOnlyList<OperationMethodDescriptor> BuildAttributeDescriptors()
    {
        var descriptors = new List<OperationMethodDescriptor>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(x => !x.IsDynamic))
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    var operations = GetAttributes<IdentityOperationAttribute>(method).ToList();
                    var serviceOperations = GetAttributes<ServiceOperationAttribute>(method).ToList();
                    var permissions = GetAttributes<IBeamPermissionAttribute>(method).ToList();
                    var resources = GetAttributes<IBeamResourceAccessAttribute>(method).ToList();
                    var operationTemplates = GetAttributes<IBeamOperationTemplateAttribute>(method).ToList();
                    var resourceTemplates = GetAttributes<IBeamResourceAccessTemplateAttribute>(method).ToList();

                    if (operations.Count == 0 &&
                        serviceOperations.Count == 0 &&
                        permissions.Count == 0 &&
                        resources.Count == 0 &&
                        operationTemplates.Count == 0 &&
                        resourceTemplates.Count == 0)
                    {
                        continue;
                    }

                    descriptors.Add(new OperationMethodDescriptor(
                        type.FullName ?? type.Name,
                        method.Name,
                        method.GetGenericArguments(),
                        operations,
                        serviceOperations,
                        permissions,
                        resources,
                        operationTemplates,
                        resourceTemplates));
                }
            }
        }

        return descriptors;
    }

    private static IEnumerable<T> GetAttributes<T>(MemberInfo member)
        where T : Attribute
    {
        try
        {
            return member.GetCustomAttributes<T>(inherit: true);
        }
        catch
        {
            return Array.Empty<T>();
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static string? ResolveTemplate(string? template, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var value = template.Trim();
        foreach (var pair in values)
            value = value.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);

        return value;
    }

    private static string InferOperationName(string methodName)
    {
        var value = methodName;
        if (value.EndsWith("Async", StringComparison.OrdinalIgnoreCase))
            value = value[..^"Async".Length];

        return value.ToLowerInvariant() switch
        {
            "get" or "read" or "find" or "list" => "read",
            "create" or "add" => "create",
            "update" or "edit" or "save" => "update",
            "delete" or "remove" => "delete",
            _ => value.ToLowerInvariant()
        };
    }

    private static string HumanizePermission(string key)
        => string.Join(' ', key
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => char.ToUpperInvariant(x[0]) + x[1..]));

    private static string CategoryFromKey(string key)
        => key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "operations";

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static int SourceOrder(AccessOperationCatalogItem item)
        => item.Source.Trim().ToLowerInvariant() switch
        {
            "attribute:template" => 0,
            "attribute" => 1,
            _ => 0
        };

    private sealed record OperationMethodDescriptor(
        string DeclaringType,
        string MethodName,
        IReadOnlyList<Type> GenericArgumentTypes,
        IReadOnlyList<IdentityOperationAttribute> Operations,
        IReadOnlyList<ServiceOperationAttribute> ServiceOperations,
        IReadOnlyList<IBeamPermissionAttribute> Permissions,
        IReadOnlyList<IBeamResourceAccessAttribute> ResourceAccesses,
        IReadOnlyList<IBeamOperationTemplateAttribute> OperationTemplates,
        IReadOnlyList<IBeamResourceAccessTemplateAttribute> ResourceAccessTemplates);
}
