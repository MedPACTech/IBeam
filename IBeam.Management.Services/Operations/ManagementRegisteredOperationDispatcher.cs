using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using IBeam.Management.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services;

public sealed class ManagementRegisteredOperationDispatcher : IManagementRegisteredOperationDispatcher
{
    private readonly ManagementOperationRegistry registry;
    private readonly ManagementRegisteredTargetAuthority authority;
    private readonly IServiceProvider provider;
    private readonly IServiceOperationExecutor operations;
    public ManagementRegisteredOperationDispatcher(ManagementOperationRegistry registry, IManagementOperationAccessService authority,
        IServiceProvider provider, IServiceOperationExecutor operations)
    {
        this.registry = registry; this.authority = authority as ManagementRegisteredTargetAuthority ?? throw new InvalidOperationException("Standard signed target authority is required.");
        this.provider = provider; this.operations = operations;
    }
    public Task<ManagementOperationDispatchResult> ExecuteAsync(ManagementOperationInvocation request, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            if (request is null || request.Body.Length > 32768) throw new ManagementOperationInvalidException();
            var owned = new ManagementOperationInvocation { Operation = request.Operation, Method = request.Method, Path = request.Path,
                Body = request.Body.ToArray(), IdempotencyKey = request.IdempotencyKey, IfMatch = request.IfMatch, Assertion = request.Assertion, ReceiptLookup = request.ReceiptLookup };
            var verified = await authority.VerifyAsync(owned, token);
            var descriptor = registry.Find(verified.Operation) ?? throw new ManagementAccessDeniedException();
            var method = typeof(ManagementRegisteredOperationDispatcher).GetMethod(nameof(Invoke), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(descriptor.Request.ContractType, descriptor.Result.ContractType);
            try { return await (Task<ManagementOperationDispatchResult>)method.Invoke(this, [descriptor, verified.Scope, owned, token])!; }
            catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }, new ServiceOperationExecutionOptions { OperationName = "management.operations.dispatch", AuditAction = "management.operations.dispatch",
            EntityName = "ManagementRegisteredOperation", PermissionEnabled = false, AuditEnabled = true }, ct);
    private Task<ManagementOperationDispatchResult> Invoke<TRequest, TResult>(ManagementOperationDescriptor descriptor,
        ManagementOperationContext scope, ManagementOperationInvocation request, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
        var handler = (IManagementOperationHandler<TRequest, TResult>)provider.GetRequiredService(descriptor.HandlerServiceType);
        var codec = new ManagementOperationPayloadCodec(registry);
        var result = request.ReceiptLookup
            ? await ((IManagementOperationReceiptHandler<TResult>)handler).ReadReceiptAsync(scope, token)
            : await handler.ExecuteAsync(scope, codec.ReadRequest<TRequest>(descriptor.Operation, request.Body), token);
        await authority.RequireAsync(descriptor.Operation, scope, token);
        byte[] bytes;
        try { bytes = codec.WriteResult(descriptor.Operation, result); }
        catch (ManagementAccessDeniedException) { throw new ManagementOperationUnavailableException(); }
        using var json = JsonDocument.Parse(bytes);
        return new ManagementOperationDispatchResult { Body = bytes, Unknown = json.RootElement.TryGetProperty("outcome", out var outcome) && outcome.ValueKind == JsonValueKind.String && outcome.GetString() == "unknown" };
    }, new ServiceOperationExecutionOptions { OperationName = descriptor.Operation, AuditAction = descriptor.AuditAction,
        EntityName = "ManagementRegisteredOperation", EntityId = scope.ResourceId, TenantId = scope.TenantId, PermissionEnabled = false, AuditEnabled = true }, ct);
}
public sealed class ManagementRegisteredOperationInfoService(IManagementOperationCapabilityService capabilities,
    ManagementOperationRegistry registry, IOptions<ManagementOperationTargetOptions> options, IOptions<ManagementDiagnosticsOptions> deployment,
    IManagementPrincipalProvider principals, IServiceOperationExecutor operations) : IManagementRegisteredOperationInfoService
{
    public Task<ManagementRegisteredOperationInfo> GetAsync(CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            _ = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Credential, ManagementRegisteredOperations.Info);
            var configured = options.Value;
            if (!configured.DedicatedApplicationRealm || configured.RealmId == Guid.Empty) throw new ManagementAccessDeniedException();
            var list = await capabilities.GetAsync(token);
            var result = new ManagementRegisteredOperationInfo(deployment.Value.ApplicationKey, deployment.Value.Environment, configured.RealmId,
                "dedicated-app", 1, list.ToArray());
            ManagementRegisteredOperationProtocol.Validate(result, registry); return result;
        }, new ServiceOperationExecutionOptions { OperationName = ManagementRegisteredOperations.Info, AuditAction = ManagementRegisteredOperations.Info,
            EntityName = "ManagementRegisteredOperationInfo", PermissionEnabled = false, AuditEnabled = true }, ct);
}
