using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IBeam.Management.CustomerIdentity;

namespace IBeam.Management.Operations;

/// <summary>Immutable, per-host catalog. Existing built-ins retain their compiled endpoints and schemas.
/// Extension descriptors cannot replace them, imply installation, or grant authority.</summary>
public sealed class ManagementOperationRegistry
{
    public const int MaximumOperations = 128;
    private readonly FrozenDictionary<string, ManagementOperationDescriptor> _extensions;
    private readonly FrozenDictionary<string, IReadOnlyList<string>> _requirements;
    public static ManagementOperationRegistry BuiltIn { get; } = new([]);
    public IReadOnlyList<string> Operations { get; }
    public IReadOnlyList<ManagementOperationDescriptor> Descriptors { get; }

    public ManagementOperationRegistry(IEnumerable<ManagementOperationDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var copy = descriptors.Take(MaximumOperations + 1).ToArray();
        if (copy.Length > MaximumOperations - ManagementCustomerIdentityOperations.All.Count - 4) Invalid();
        var entries = new Dictionary<string, ManagementOperationDescriptor>(StringComparer.Ordinal);
        var requirements = ManagementCustomerIdentityOperations.All.ToDictionary(x => x,
            x => x == ManagementCustomerIdentityOperations.Info ? (IReadOnlyList<string>)Array.AsReadOnly(new[] { x }) :
                ManagementCustomerIdentityPermissionPolicy.RequiredFor(x), StringComparer.Ordinal);
        foreach (var op in new[] { ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead })
            requirements.Add(op, Array.AsReadOnly(new[] { op }));
        requirements.Add(ManagementRegisteredOperations.Info, Array.AsReadOnly(new[] { ManagementRegisteredOperations.Info }));
        foreach (var descriptor in copy)
        {
            Validate(descriptor);
            if (requirements.ContainsKey(descriptor.Operation) || !entries.TryAdd(descriptor.Operation,
                descriptor with { RequiredGrants = Array.AsReadOnly(descriptor.RequiredGrants.ToArray()) })) Invalid();
        }
        if (entries.Values.Select(x => x.HandlerServiceType).Distinct().Count() != entries.Count) Invalid();
        var schemas = new Dictionary<(string, int), Type>();
        foreach (var schema in entries.Values.SelectMany(x => new[] { x.Request, x.Result }))
        {
            var key = (schema.Id, schema.Version);
            if (schemas.TryGetValue(key, out var contract) && contract != schema.ContractType) Invalid();
            schemas[key] = schema.ContractType;
        }
        var routes = entries.Values.ToArray();
        for (var i = 0; i < routes.Length; i++)
        for (var j = i + 1; j < routes.Length; j++)
            if (routes[i].Method == routes[j].Method && Overlaps(routes[i].RouteTemplate, routes[j].RouteTemplate)) Invalid();
        foreach (var entry in entries.Values)
        {
            if (entry.RequiredGrants.Any(x => x != entry.Operation && !requirements.ContainsKey(x) && !entries.ContainsKey(x))) Invalid();
            requirements.Add(entry.Operation, entry.RequiredGrants);
        }
        // Grant closure is computed once; cyclic dependencies are a registration error.
        var expanded = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        IReadOnlyList<string> Expand(string name, HashSet<string> visiting)
        {
            if (expanded.TryGetValue(name, out var found)) return found;
            if (!visiting.Add(name)) { Invalid(); }
            var all = new HashSet<string>(StringComparer.Ordinal) { name };
            foreach (var child in requirements[name].Where(x => x != name)) all.UnionWith(Expand(child, visiting));
            visiting.Remove(name);
            return expanded[name] = Array.AsReadOnly(all.Order(StringComparer.Ordinal).ToArray());
        }
        foreach (var name in requirements.Keys) Expand(name, new(StringComparer.Ordinal));
        _extensions = entries.ToFrozenDictionary(StringComparer.Ordinal);
        _requirements = expanded.ToFrozenDictionary(StringComparer.Ordinal);
        Operations = Array.AsReadOnly(_requirements.Keys.Order(StringComparer.Ordinal).ToArray());
        Descriptors = Array.AsReadOnly(entries.Values.OrderBy(x => x.Operation, StringComparer.Ordinal).ToArray());
    }

    public bool Contains(string operation) => operation is not null && _requirements.ContainsKey(operation);
    public bool IsAssignable(string operation) => Contains(operation) && operation is not
        (ManagementOperations.DiagnosticsInfo or ManagementCustomerIdentityOperations.Info or ManagementRegisteredOperations.Info);
    public IReadOnlyList<string> RequiredFor(string operation) => IsAssignable(operation)
        ? _requirements[operation] : throw new ManagementAccessDeniedException();
    public ManagementOperationDescriptor? Find(string operation) => _extensions.GetValueOrDefault(operation);
    public string DelegationFor(string operation, Guid applicationId)
    {
        if (!IsAssignable(operation) || applicationId == Guid.Empty) throw new ManagementAccessDeniedException();
        return operation == ManagementOperations.ErrorsRead ? ManagementAccessDelegation.Errors :
            operation == ManagementOperations.LogsRead ? ManagementAccessDelegation.Logs :
            "management.application-access.delegate." + operation["management.".Length..] + ":" + applicationId.ToString("N");
    }
    public bool ValidCapabilities(IReadOnlyList<string>? values) => values is { Count: > 0 } && values.Count <= Operations.Count &&
        values.All(Contains) && values.Distinct(StringComparer.Ordinal).Count() == values.Count;
    public bool AllowsAudit(string action) => action is not null && _extensions.Values.Any(x => x.AuditAction == action);

    /// <summary>Only installed, compatible handlers whose entire explicit grant matrix is enabled are discoverable.</summary>
    public IReadOnlyList<ManagementOperationCapability> Capabilities(IReadOnlyList<string> credentialGrants,
        IReadOnlyList<string> enabledOperations, Func<Type, bool> isInstalled)
    {
        ArgumentNullException.ThrowIfNull(isInstalled);
        if (!ValidCapabilities(credentialGrants) || enabledOperations is null ||
            enabledOperations.Count > Operations.Count || enabledOperations.Any(x => !IsAssignable(x)) ||
            enabledOperations.Distinct(StringComparer.Ordinal).Count() != enabledOperations.Count) throw new ManagementAccessDeniedException();
        return Array.AsReadOnly(Descriptors.Where(x => RequiredFor(x.Operation).All(required =>
            credentialGrants.Contains(required, StringComparer.Ordinal) && enabledOperations.Contains(required, StringComparer.Ordinal)) &&
            isInstalled(x.HandlerServiceType)).Select(x => new ManagementOperationCapability(x.Operation, x.ProtocolVersion,
                x.Request.Id, x.Request.Version, x.Result.Id, x.Result.Version, x.ReceiptProtocolVersion) { DescriptorFingerprint = Fingerprint(x) }).ToArray());
    }
    public static string Fingerprint(ManagementOperationDescriptor descriptor) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new { descriptor.Operation, descriptor.Method, descriptor.RouteTemplate,
            RequestSchema = descriptor.Request.Id, RequestVersion = descriptor.Request.Version,
            ResultSchema = descriptor.Result.Id, ResultVersion = descriptor.Result.Version,
            Grants = descriptor.RequiredGrants.Order(StringComparer.Ordinal).ToArray(), descriptor.ReceiptProtocolVersion,
            descriptor.AuditAction, descriptor.MaximumRequestBytes, descriptor.MaximumResultBytes, descriptor.ProtocolVersion })));

    /// <summary>Validate a concrete extension binding; query parameters and percent-encoded path components are not accepted.</summary>
    public void RequireBinding(string operation, string method, string path, int bodyBytes)
    {
        var entry = Find(operation) ?? throw new ManagementAccessDeniedException();
        if (entry.Method != method || bodyBytes < 0 || bodyBytes > entry.MaximumRequestBytes ||
            method == "GET" && bodyBytes != 0 || path is null || path.Length > 2048 || !Matches(entry.RouteTemplate, path))
            throw new ManagementAccessDeniedException();
    }
    private static bool Matches(string template, string path)
    {
        var expected = template.Split('/'); var actual = path.Split('/');
        return expected.Length == actual.Length && expected.Zip(actual).All(x => Slot(x.First)
            ? Guid.TryParseExact(x.Second, "D", out var id) && id != Guid.Empty : x.First == x.Second);
    }
    private static bool Overlaps(string first, string second)
    {
        var a = first.Split('/'); var b = second.Split('/');
        return a.Length == b.Length && a.Zip(b).All(x => x.First == x.Second || Slot(x.First) || Slot(x.Second));
    }
    private static bool Slot(string segment) => segment.StartsWith('{') && segment.EndsWith(":guid}");
    private static void Validate(ManagementOperationDescriptor d)
    {
        if (d is null || d.ProtocolVersion != 1 || !Name(d.Operation) ||
            d.Operation.StartsWith("management.identity.", StringComparison.Ordinal) || d.Operation.StartsWith("management.diagnostics.", StringComparison.Ordinal) ||
            d.Method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE") ||
            d.RouteTemplate is not { Length: > 0 and <= 256 } ||
            !(d.RouteTemplate.StartsWith("/api/management-operations/", StringComparison.Ordinal) || NativeExport(d)) ||
            d.Operation == ManagementTenantDataOperations.Export && !NativeExport(d) ||
            d.RouteTemplate == "/api/management-operations/info" || d.RouteTemplate.StartsWith("/api/management-operations/receipts/", StringComparison.Ordinal) ||
            d.RouteTemplate.Split('/').Skip(1).Any(x => !Regex.IsMatch(x, "^[a-z][a-z0-9-]{0,63}$|^\\{[a-z][a-zA-Z0-9]{0,31}:guid\\}$", RegexOptions.CultureInvariant)) ||
            d.RouteTemplate.Split('/').Where(Slot).Distinct(StringComparer.Ordinal).Count() != d.RouteTemplate.Split('/').Count(Slot) ||
            d.RequiredGrants is not { Count: > 0 and <= 32 } || !d.RequiredGrants.Contains(d.Operation, StringComparer.Ordinal) ||
            d.RequiredGrants.Any(x => !Name(x)) || d.RequiredGrants.Distinct(StringComparer.Ordinal).Count() != d.RequiredGrants.Count ||
            d.AuditAction != d.Operation || d.ReceiptProtocolVersion != (d.Method == "GET" ? 0 : 1) ||
            d.MaximumRequestBytes is < 1 or > 32768 || d.MaximumResultBytes is < 1 or > 262144) Invalid();
        if (d!.Request is null || d.Result is null || d.HandlerServiceType is null || !d.HandlerServiceType.IsInterface ||
            !d.HandlerServiceType.IsGenericType || d.HandlerServiceType.GetGenericTypeDefinition() != typeof(IManagementOperationHandler<,>) ||
            !d.HandlerServiceType.GetGenericArguments().SequenceEqual(new[] { d.Request.ContractType, d.Result.ContractType })) Invalid();
        Schema(d.Request!); Schema(d.Result!);
    }
    private static bool Name(string? name) => name is { Length: <= 128 } && Regex.IsMatch(name,
        "^management\\.[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9-]*){1,5}$", RegexOptions.CultureInvariant);
    private static bool NativeExport(ManagementOperationDescriptor d) => d.Operation == ManagementTenantDataOperations.Export && d.Method == "POST" &&
        d.RouteTemplate == TenantData.ManagementTenantExportRoutes.Submit && d.Request?.ContractType == typeof(TenantData.ManagementTenantExportRequest) &&
        d.Result?.ContractType == typeof(TenantData.ManagementTenantExportResult) && d.RequiredGrants is { Count: 1 } && d.RequiredGrants[0] == ManagementTenantDataOperations.Export;
    private static void Schema(ManagementOperationSchema schema)
    {
        if (schema.Version != 1 || schema.Id is not { Length: > 0 and <= 100 } ||
            !Regex.IsMatch(schema.Id, "^[a-z][a-z0-9.-]+$", RegexOptions.CultureInvariant) ||
            schema.ContractType is null || !schema.ContractType.IsClass || !schema.ContractType.IsSealed ||
            schema.ContractType == typeof(string) || schema.ContractType.IsArray) Invalid();
        var count = 0;
        void Visit(Type? type, int depth, HashSet<Type> path)
        {
            if (type is null || depth > 4 || ++count > 64) Invalid();
            type = Nullable.GetUnderlyingType(type!) ?? type!;
            if (type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long) ||
                type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTimeOffset) || type.IsEnum) return;
            if (type.IsArray && type.GetArrayRank() == 1 && type.GetElementType() != typeof(byte)) { Visit(type.GetElementType(), depth + 1, path); return; }
            if (!type.IsSealed || !type.IsClass || !type.IsPublic && !type.IsNestedPublic || type.IsGenericType ||
                type.Assembly == typeof(object).Assembly || !path.Add(type) ||
                type.GetCustomAttributes().Any(x => x is JsonConverterAttribute or JsonPolymorphicAttribute or JsonDerivedTypeAttribute)) Invalid();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            if (properties.Length > 32 || type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length != 0) Invalid();
            var names = properties.Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToArray();
            if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) Invalid();
            foreach (var p in properties)
            {
                if (p.GetIndexParameters().Length != 0 || p.GetMethod is null || p.SetMethod is null ||
                    p.GetCustomAttributes().Any(x => x is JsonExtensionDataAttribute or JsonConverterAttribute)) Invalid();
                Visit(p.PropertyType, depth + 1, path);
            }
            path.Remove(type);
        }
        Visit(schema.ContractType, 0, []);
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new ArgumentException("Invalid management operation registration.");
}
