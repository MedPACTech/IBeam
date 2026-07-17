using IBeam.AccessControl;
using IBeam.Repositories.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;

namespace IBeam.Services.Abstractions;

public sealed class ServiceOperationExecutor : IServiceOperationExecutor
{
    private readonly IAuditTrailSink _auditTrailSink;
    private readonly IAuditActorProvider _auditActorProvider;
    private readonly IAuditRequestContextProvider _auditRequestContextProvider;
    private readonly IServiceOperationAuthorizer? _serviceOperationAuthorizer;
    private readonly IServiceOperationPrincipalProvider _serviceOperationPrincipalProvider;
    private readonly IOptionsMonitor<ServiceAuditOptions> _auditOptionsMonitor;
    private readonly ITenantContext? _tenantContext;

    public ServiceOperationExecutor(
        IAuditTrailSink? auditTrailSink = null,
        IAuditActorProvider? auditActorProvider = null,
        IAuditRequestContextProvider? auditRequestContextProvider = null,
        IServiceOperationAuthorizer? serviceOperationAuthorizer = null,
        IServiceOperationPrincipalProvider? serviceOperationPrincipalProvider = null,
        IOptionsMonitor<ServiceAuditOptions>? auditOptionsMonitor = null,
        ITenantContext? tenantContext = null)
    {
        _auditTrailSink = auditTrailSink ?? new NoOpAuditTrailSink();
        _auditActorProvider = auditActorProvider ?? new NoOpAuditActorProvider();
        _auditRequestContextProvider = auditRequestContextProvider ?? new NoOpAuditRequestContextProvider();
        _serviceOperationAuthorizer = serviceOperationAuthorizer;
        _serviceOperationPrincipalProvider = serviceOperationPrincipalProvider ?? new NoOpServiceOperationPrincipalProvider();
        _auditOptionsMonitor = auditOptionsMonitor ?? new StaticOptionsMonitor<ServiceAuditOptions>(new ServiceAuditOptions());
        _tenantContext = tenantContext;
    }

    public async Task ExecuteAsync(
        object serviceInstance,
        Func<CancellationToken, Task> operation,
        ServiceOperationExecutionOptions? options = null,
        CancellationToken ct = default,
        [CallerMemberName]
        string? callerMemberName = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteAsync<object?>(
            serviceInstance,
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return null;
            },
            options,
            ct,
            callerMemberName).ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        object serviceInstance,
        Func<CancellationToken, Task<TResult>> operation,
        ServiceOperationExecutionOptions? options = null,
        CancellationToken ct = default,
        [CallerMemberName]
        string? callerMemberName = null)
    {
        ArgumentNullException.ThrowIfNull(serviceInstance);
        ArgumentNullException.ThrowIfNull(operation);

        var resolved = ResolveOperation(serviceInstance, options, callerMemberName);
        var watch = Stopwatch.StartNew();

        try
        {
            await DemandAccessAsync(resolved, ct).ConfigureAwait(false);
            var result = await operation(ct).ConfigureAwait(false);
            watch.Stop();
            await TryWriteOperationAuditAsync(resolved, true, watch.ElapsedMilliseconds, null, ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            watch.Stop();
            await TryWriteOperationAuditAsync(resolved, false, watch.ElapsedMilliseconds, ex, ct).ConfigureAwait(false);
            throw;
        }
    }

    public void Execute(
        object serviceInstance,
        Action operation,
        ServiceOperationExecutionOptions? options = null,
        [CallerMemberName]
        string? callerMemberName = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Execute<object?>(
            serviceInstance,
            () =>
            {
                operation();
                return null;
            },
            options,
            callerMemberName);
    }

    public TResult Execute<TResult>(
        object serviceInstance,
        Func<TResult> operation,
        ServiceOperationExecutionOptions? options = null,
        [CallerMemberName]
        string? callerMemberName = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteAsync(
                serviceInstance,
                _ => Task.FromResult(operation()),
                options,
                CancellationToken.None,
                callerMemberName)
            .GetAwaiter()
            .GetResult();
    }

    private async Task DemandAccessAsync(ResolvedServiceOperation operation, CancellationToken ct)
    {
        if (!operation.PermissionEnabled || _serviceOperationAuthorizer is null)
        {
            return;
        }

        var tenantId = operation.TenantId ?? _tenantContext?.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new AccessControlException("tenantId is required for service operation authorization.");
        }

        var principal = _serviceOperationPrincipalProvider.GetPrincipal()
            ?? new ClaimsPrincipal(new ClaimsIdentity());

        foreach (var permissionName in operation.PermissionNames)
        {
            var result = await _serviceOperationAuthorizer.AuthorizeAsync(
                new ServiceOperationAuthorizationRequest(tenantId.Value, principal, permissionName),
                ct).ConfigureAwait(false);

            if (!result.Allowed)
            {
                throw new UnauthorizedAccessException($"Access denied for service operation '{permissionName}'.");
            }
        }
    }

    private async Task TryWriteOperationAuditAsync(
        ResolvedServiceOperation operation,
        bool succeeded,
        long durationMs,
        Exception? exception,
        CancellationToken ct)
    {
        var options = _auditOptionsMonitor.CurrentValue;
        if (!ShouldWriteTransactionAudit(operation, options))
        {
            return;
        }

        var requestContext = _auditRequestContextProvider.GetContext();
        var txn = new ServiceAuditTransaction
        {
            ServiceName = operation.ServiceName,
            EntityName = operation.EntityName,
            Operation = operation.AuditOperation,
            Action = operation.AuditAction,
            EntityId = operation.EntityId,
            TenantId = operation.TenantId ?? _tenantContext?.TenantId,
            ActorId = _auditActorProvider.GetActorId(),
            CorrelationId = requestContext.CorrelationId,
            IpAddress = requestContext.IpAddress,
            UserAgent = requestContext.UserAgent,
            DeviceId = requestContext.DeviceId,
            OriginalJson = CaptureBefore(operation, options) ? operation.OriginalJson : null,
            TransformedJson = CaptureAfter(operation, options) ? operation.TransformedJson : null,
            OccurredUtc = DateTimeOffset.UtcNow,
            Succeeded = succeeded,
            ErrorType = exception?.GetType().FullName,
            ErrorMessage = exception?.Message,
            DurationMs = durationMs
        };

        try
        {
            await _auditTrailSink.WriteTransactionAsync(txn, ct).ConfigureAwait(false);
        }
        catch when (!options.FailOnAuditError)
        {
            // Keep service flow resilient by default.
        }
    }

    private static bool ShouldWriteTransactionAudit(ResolvedServiceOperation operation, ServiceAuditOptions options)
    {
        if (!operation.AuditEnabled || !options.Enabled)
        {
            return false;
        }

        var serviceOptions = ResolveAuditServiceOptions(operation, options);
        if (serviceOptions?.Enabled == false)
        {
            return false;
        }

        var operationOptions = ResolveAuditOperationOptions(operation, options);
        if (operationOptions?.Enabled is bool operationEnabled)
        {
            return operationEnabled;
        }

        if (serviceOptions?.Enabled == true)
        {
            return true;
        }

        return options.DefaultMode == ServiceAuditDefaultMode.AuditWrites;
    }

    private static bool CaptureBefore(ResolvedServiceOperation operation, ServiceAuditOptions options)
        => ResolveAuditOperationOptions(operation, options)?.CaptureBefore ?? options.CaptureBefore;

    private static bool CaptureAfter(ResolvedServiceOperation operation, ServiceAuditOptions options)
        => ResolveAuditOperationOptions(operation, options)?.CaptureAfter ?? options.CaptureAfter;

    private static ServiceAuditServiceOptions? ResolveAuditServiceOptions(
        ResolvedServiceOperation operation,
        ServiceAuditOptions options)
    {
        if (options.Services.TryGetValue(operation.ServiceType.FullName ?? string.Empty, out var full))
        {
            return full;
        }

        return options.Services.TryGetValue(operation.ServiceType.Name, out var shortName) ? shortName : null;
    }

    private static ServiceAuditOperationOptions? ResolveAuditOperationOptions(
        ResolvedServiceOperation operation,
        ServiceAuditOptions options)
    {
        var serviceOptions = ResolveAuditServiceOptions(operation, options);
        if (serviceOptions is null)
        {
            return null;
        }

        if (serviceOptions.Operations.TryGetValue(operation.AuditOperation.ToString(), out var byOperation))
        {
            return byOperation;
        }

        if (serviceOptions.Operations.TryGetValue(operation.OperationName, out var byOperationName))
        {
            return byOperationName;
        }

        return serviceOptions.Operations.TryGetValue(operation.AuditAction, out var byAction) ? byAction : null;
    }

    private static ResolvedServiceOperation ResolveOperation(
        object serviceInstance,
        ServiceOperationExecutionOptions? options,
        string? callerMemberName)
    {
        options ??= new ServiceOperationExecutionOptions();

        var serviceType = serviceInstance.GetType();
        var method = ResolveMethod(serviceType, callerMemberName);

        var methodOperation = LastAttribute<IBeamOperationAttribute>(method);
        var classOperation = LastAttribute<IBeamOperationAttribute>(serviceType);
        var operationAttribute = methodOperation ?? classOperation;

        var methodAudit = LastAttribute<IBeamAuditActionAttribute>(method);
        var classAudit = LastAttribute<IBeamAuditActionAttribute>(serviceType);
        var auditAttribute = methodAudit ?? classAudit;

        var operationName = FirstNonBlank(
            options.OperationName,
            operationAttribute?.Name,
            BuildDefaultOperationName(serviceType, callerMemberName));

        var auditEnabled = options.AuditEnabled
            ?? (auditAttribute?.Enabled ?? operationAttribute?.Audit ?? true);

        var permissionEnabled = options.PermissionEnabled
            ?? (operationAttribute?.Permission ?? true);

        var auditAction = FirstNonBlank(
            options.AuditAction,
            auditAttribute is { Enabled: true } ? auditAttribute.Action : null,
            operationAttribute?.AuditAction,
            operationAttribute is { Audit: true } && operationAttribute.Name.Contains('.', StringComparison.Ordinal) ? operationAttribute.Name : null,
            operationName);

        var entityName = FirstNonBlank(
            options.EntityName,
            methodOperation?.Name.Contains('.', StringComparison.Ordinal) == false ? methodOperation.Name : null,
            classOperation?.Name.Contains('.', StringComparison.Ordinal) == false ? classOperation.Name : null,
            NormalizeEntityName(serviceType.Name));

        var permissions = new List<string>();
        permissions.AddRange(options.RequiredPermissionNames.Where(x => !string.IsNullOrWhiteSpace(x)));
        permissions.AddRange(Attributes<IBeamRequiresPermissionAttribute>(serviceType).Select(x => x.PermissionName));
        permissions.AddRange(Attributes<IBeamRequiresPermissionAttribute>(method).Select(x => x.PermissionName));

        if (!string.IsNullOrWhiteSpace(operationAttribute?.PermissionName))
        {
            permissions.Add(operationAttribute.PermissionName);
        }

        if (permissions.Count == 0)
        {
            permissions.Add(operationName);
        }

        return new ResolvedServiceOperation(
            serviceType,
            serviceType.Name,
            callerMemberName,
            operationName,
            auditAction,
            entityName,
            options.AuditOperation,
            options.EntityId,
            options.TenantId,
            auditEnabled,
            permissionEnabled,
            permissions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ResolveJson(options.OriginalJson, options.OriginalData),
            ResolveJson(options.TransformedJson, options.TransformedData));
    }

    private static MethodInfo? ResolveMethod(Type serviceType, string? methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            return null;
        }

        return serviceType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(x => string.Equals(x.Name, methodName, StringComparison.Ordinal))
            .OrderByDescending(x => Attributes<IBeamOperationAttribute>(x).Any() ? 1 : 0)
            .ThenByDescending(x => Attributes<IBeamAuditActionAttribute>(x).Any() ? 1 : 0)
            .ThenByDescending(x => Attributes<IBeamRequiresPermissionAttribute>(x).Any() ? 1 : 0)
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

    private static string? ResolveJson(string? json, object? data)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        return data is null ? null : JsonSerializer.Serialize(data);
    }

    private sealed record ResolvedServiceOperation(
        Type ServiceType,
        string ServiceName,
        string? MethodName,
        string OperationName,
        string AuditAction,
        string EntityName,
        ServiceAuditOperation AuditOperation,
        Guid? EntityId,
        Guid? TenantId,
        bool AuditEnabled,
        bool PermissionEnabled,
        IReadOnlyList<string> PermissionNames,
        string? OriginalJson,
        string? TransformedJson);

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
