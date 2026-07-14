using System.Reflection;
using IBeam.Identity.Authorization;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Authorization;

public sealed class PermissionCatalogProvider : IPermissionCatalogProvider
{
    private readonly IOptionsMonitor<PermissionAccessOptions> _options;
    private readonly Lazy<IReadOnlyList<ExposedPermission>> _attributeCatalog;

    public PermissionCatalogProvider(IOptionsMonitor<PermissionAccessOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _attributeCatalog = new Lazy<IReadOnlyList<ExposedPermission>>(BuildAttributeCatalog, isThreadSafe: true);
    }

    public Task<IReadOnlyList<ExposedPermission>> GetExposedPermissionsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var all = new List<ExposedPermission>();
        all.AddRange(_attributeCatalog.Value);
        all.AddRange(BuildConfigurationCatalog(_options.CurrentValue));
        all.AddRange(BuildMappingCatalog(_options.CurrentValue));

        var deduped = all
            .Where(HasPermissionKey)
            .GroupBy(PermissionKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.PermissionName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PermissionId ?? Guid.Empty)
            .ToList();

        return Task.FromResult<IReadOnlyList<ExposedPermission>>(deduped);
    }

    private static IReadOnlyList<ExposedPermission> BuildAttributeCatalog()
    {
        var list = new List<ExposedPermission>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(x => !x.IsDynamic))
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                var classResource = type.FullName ?? type.Name;

                foreach (var name in GetPermissionNames(type))
                {
                    list.Add(new ExposedPermission(name, null, "attribute:class", classResource));
                }

                foreach (var id in GetPermissionIds(type))
                {
                    list.Add(new ExposedPermission(null, id, "attribute:class", classResource));
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    var methodResource = $"{classResource}.{method.Name}";

                    foreach (var name in GetPermissionNames(method))
                    {
                        list.Add(new ExposedPermission(name, null, "attribute:method", methodResource));
                    }

                    foreach (var id in GetPermissionIds(method))
                    {
                        list.Add(new ExposedPermission(null, id, "attribute:method", methodResource));
                    }
                }
            }
        }

        return list;
    }

    private static IReadOnlyList<ExposedPermission> BuildConfigurationCatalog(PermissionAccessOptions options)
    {
        if (options.Catalog is null || options.Catalog.Count == 0)
            return Array.Empty<ExposedPermission>();

        return options.Catalog
            .Where(x => x is not null)
            .Select(x => new ExposedPermission(
                PermissionName: string.IsNullOrWhiteSpace(x.PermissionName) ? null : x.PermissionName.Trim(),
                PermissionId: x.PermissionId,
                Source: "configuration:catalog",
                Resource: string.IsNullOrWhiteSpace(x.Resource) ? "Configuration" : x.Resource.Trim(),
                Description: string.IsNullOrWhiteSpace(x.Description) ? null : x.Description.Trim(),
                Label: string.IsNullOrWhiteSpace(x.Label) ? null : x.Label.Trim(),
                Category: string.IsNullOrWhiteSpace(x.Category) ? null : x.Category.Trim(),
                IsAssignable: x.IsAssignable,
                ModuleKey: string.IsNullOrWhiteSpace(x.ModuleKey) ? null : x.ModuleKey.Trim(),
                ResourceType: string.IsNullOrWhiteSpace(x.ResourceType) ? null : x.ResourceType.Trim(),
                ResourceId: string.IsNullOrWhiteSpace(x.ResourceId) ? null : x.ResourceId.Trim(),
                AccessLevel: string.IsNullOrWhiteSpace(x.AccessLevel) ? null : x.AccessLevel.Trim()))
            .Where(HasPermissionKey)
            .ToList();
    }

    private static IReadOnlyList<ExposedPermission> BuildMappingCatalog(PermissionAccessOptions options)
    {
        if (options.Mappings is null || options.Mappings.Count == 0)
            return Array.Empty<ExposedPermission>();

        return options.Mappings
            .Where(x => x is not null)
            .Select(x => new ExposedPermission(
                PermissionName: string.IsNullOrWhiteSpace(x.PermissionName) ? null : x.PermissionName.Trim(),
                PermissionId: x.PermissionId,
                Source: "configuration:mapping",
                Resource: "PermissionAccessOptions.Mappings"))
            .Where(HasPermissionKey)
            .ToList();
    }

    private static bool HasPermissionKey(ExposedPermission p)
        => !string.IsNullOrWhiteSpace(p.PermissionName) || p.PermissionId.HasValue;

    private static string PermissionKey(ExposedPermission p)
        => !string.IsNullOrWhiteSpace(p.PermissionName)
            ? $"nam|{p.PermissionName!.Trim()}"
            : $"id|{p.PermissionId!.Value:D}";

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

    private static IEnumerable<string> GetPermissionNames(MemberInfo member)
    {
        try
        {
            return member
                .GetCustomAttributes<PermissionAccessAttribute>(inherit: true)
                .SelectMany(x => x.PermissionNames)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<Guid> GetPermissionIds(MemberInfo member)
    {
        try
        {
            return member
                .GetCustomAttributes<PermissionAccessIdAttribute>(inherit: true)
                .SelectMany(x => x.PermissionIds)
                .Where(x => Guid.TryParse(x, out _))
                .Select(Guid.Parse)
                .Distinct()
                .ToList();
        }
        catch
        {
            return Array.Empty<Guid>();
        }
    }
}
