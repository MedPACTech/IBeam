using IBeam.AccessControl;
using IBeam.Repositories.Abstractions;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace IBeam.Licensing.Services;

public sealed class LicensedServiceOperationExecutor : IServiceOperationExecutor
{
    private readonly ILicenseGate _gate;
    private readonly ILicenseSubjectResolver _subjectResolver;
    private readonly IServiceOperationPrincipalProvider _principalProvider;
    private readonly ITenantContext? _tenantContext;
    private readonly ServiceOperationExecutor _inner;

    public LicensedServiceOperationExecutor(
        ILicenseGate gate,
        ILicenseSubjectResolver? subjectResolver = null,
        IAuditTrailSink? auditTrailSink = null,
        IAuditActorProvider? auditActorProvider = null,
        IAuditRequestContextProvider? auditRequestContextProvider = null,
        IServiceOperationAuthorizer? serviceOperationAuthorizer = null,
        IServiceOperationPrincipalProvider? serviceOperationPrincipalProvider = null,
        IOptionsMonitor<ServiceAuditOptions>? auditOptionsMonitor = null,
        ITenantContext? tenantContext = null)
    {
        _gate = gate;
        _subjectResolver = subjectResolver ?? new ClaimsPrincipalLicenseSubjectResolver();
        _principalProvider = serviceOperationPrincipalProvider ?? new NoOpServiceOperationPrincipalProvider();
        _tenantContext = tenantContext;
        _inner = new ServiceOperationExecutor(
            auditTrailSink,
            auditActorProvider,
            auditRequestContextProvider,
            serviceOperationAuthorizer,
            _principalProvider,
            auditOptionsMonitor,
            tenantContext);
    }

    public async Task ExecuteAsync(
        object serviceInstance,
        Func<CancellationToken, Task> operation,
        ServiceOperationExecutionOptions? options = null,
        CancellationToken ct = default,
        [CallerMemberName] string? callerMemberName = null)
    {
        await RequireLicenseAsync(serviceInstance, options, callerMemberName, ct).ConfigureAwait(false);
        await _inner.ExecuteAsync(serviceInstance, operation, options, ct, callerMemberName).ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        object serviceInstance,
        Func<CancellationToken, Task<TResult>> operation,
        ServiceOperationExecutionOptions? options = null,
        CancellationToken ct = default,
        [CallerMemberName] string? callerMemberName = null)
    {
        await RequireLicenseAsync(serviceInstance, options, callerMemberName, ct).ConfigureAwait(false);
        return await _inner.ExecuteAsync(serviceInstance, operation, options, ct, callerMemberName).ConfigureAwait(false);
    }

    public void Execute(
        object serviceInstance,
        Action operation,
        ServiceOperationExecutionOptions? options = null,
        [CallerMemberName] string? callerMemberName = null)
    {
        RequireLicenseAsync(serviceInstance, options, callerMemberName, CancellationToken.None).GetAwaiter().GetResult();
        _inner.Execute(serviceInstance, operation, options, callerMemberName);
    }

    public TResult Execute<TResult>(
        object serviceInstance,
        Func<TResult> operation,
        ServiceOperationExecutionOptions? options = null,
        [CallerMemberName] string? callerMemberName = null)
    {
        RequireLicenseAsync(serviceInstance, options, callerMemberName, CancellationToken.None).GetAwaiter().GetResult();
        return _inner.Execute(serviceInstance, operation, options, callerMemberName);
    }

    private async Task RequireLicenseAsync(
        object serviceInstance,
        ServiceOperationExecutionOptions? options,
        string? callerMemberName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(serviceInstance);

        var serviceType = serviceInstance.GetType();
        var method = ResolveMethod(serviceType, callerMemberName);
        var entitlement = LastAttribute<IBeamRequiresEntitlementAttribute>(method)
                          ?? LastAttribute<IBeamRequiresEntitlementAttribute>(serviceType);
        if (entitlement is null)
            return;

        var tenantId = options?.TenantId ?? _tenantContext?.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            throw new LicensingException("tenantId is required for licensed service operation access.");

        var principal = _principalProvider.GetPrincipal()
                        ?? new ClaimsPrincipal(new ClaimsIdentity());
        var subject = _subjectResolver.ResolveSubject(principal);
        if (subject is null)
            throw new LicensingException("A license subject could not be resolved for the current service operation principal.");

        await _gate.RequireAsync(
            new LicenseGateRequest
            {
                TenantId = tenantId.Value,
                Subject = subject,
                Entitlement = entitlement.Entitlement,
                OperationName = ResolveOperationName(serviceType, method, options, callerMemberName)
            },
            ct).ConfigureAwait(false);
    }

    private static string ResolveOperationName(
        Type serviceType,
        MethodInfo? method,
        ServiceOperationExecutionOptions? options,
        string? callerMemberName)
        => FirstNonBlank(
            options?.OperationName,
            LastAttribute<IBeamOperationAttribute>(method)?.Name,
            LastAttribute<IBeamOperationAttribute>(serviceType)?.Name,
            BuildDefaultOperationName(serviceType, callerMemberName));

    private static MethodInfo? ResolveMethod(Type serviceType, string? methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
            return null;

        return serviceType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(x => string.Equals(x.Name, methodName, StringComparison.Ordinal))
            .OrderByDescending(x => Attributes<IBeamRequiresEntitlementAttribute>(x).Any() ? 1 : 0)
            .ThenByDescending(x => Attributes<IBeamOperationAttribute>(x).Any() ? 1 : 0)
            .FirstOrDefault();
    }

    private static IEnumerable<T> Attributes<T>(MemberInfo? member)
        where T : Attribute
        => member is null
            ? []
            : member.GetCustomAttributes(typeof(T), inherit: true).OfType<T>();

    private static T? LastAttribute<T>(MemberInfo? member)
        where T : Attribute
        => Attributes<T>(member).LastOrDefault();

    private static string FirstNonBlank(params string?[] values)
        => values.First(x => !string.IsNullOrWhiteSpace(x))!.Trim();

    private static string BuildDefaultOperationName(Type serviceType, string? methodName)
        => $"{NormalizeEntityName(serviceType.Name)}.{NormalizeMethodName(methodName ?? "execute")}";

    private static string NormalizeEntityName(string name)
    {
        var value = name.EndsWith("Service", StringComparison.OrdinalIgnoreCase)
            ? name[..^"Service".Length]
            : name;

        value = value.EndsWith("Entity", StringComparison.OrdinalIgnoreCase)
            ? value[..^"Entity".Length]
            : value;

        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeMethodName(string methodName)
    {
        var value = methodName.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
            ? methodName[..^"Async".Length]
            : methodName;

        return value.Trim().ToLowerInvariant();
    }
}
