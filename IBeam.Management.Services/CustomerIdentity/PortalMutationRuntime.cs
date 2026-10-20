using IBeam.Management.CustomerIdentity;
using IBeam.Management.Models;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed class PortalCustomerMutationOptions
{
    public const string SectionName = "IBeam:Management:PortalCustomerIdentity:Mutations";
    public int AttemptTimeoutSeconds { get; set; } = 15;
    public int AuthorizationTimeoutSeconds { get; set; } = 5;
    public bool IsValid() => AttemptTimeoutSeconds is >= 1 and <= 20 && AuthorizationTimeoutSeconds is >= 1 and <= 5 &&
        AttemptTimeoutSeconds + 3 * AuthorizationTimeoutSeconds <= 30;
}
/// <summary>Bounded phases leave a fresh authorization budget after an uncertain target attempt.</summary>
public sealed class PortalMutationRuntime(IManagementCustomerIdentityAuthorizationService authorization, IManagementCustomerIdentityConnectionResolver connections,
    IOptions<PortalCustomerMutationOptions> options, IOptions<ManagementPortalCustomerDirectoryOptions> transport, IServiceOperationExecutor operations)
{
    internal CancellationTokenSource Attempt(CancellationToken ct) => Deadline(ct, options.Value.AttemptTimeoutSeconds);
    private static CancellationTokenSource Deadline(CancellationToken ct, int seconds)
    { var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(seconds)); return deadline; }
    internal async Task Authorize(Guid app, string operation, CancellationToken ct)
    { using var deadline = Deadline(ct, options.Value.AuthorizationTimeoutSeconds); await authorization.RequireAsync(app, operation, deadline.Token); }
    internal async Task<ManagementCustomerIdentityConnection> Resolve(Guid app, string operation, CancellationToken ct)
    { using var deadline = Deadline(ct, options.Value.AuthorizationTimeoutSeconds); return await connections.ResolveAsync(app, operation, deadline.Token); }
    internal async Task Recheck(ManagementCustomerIdentityConnection c, string operation, CancellationToken ct)
    { using var deadline = Deadline(ct, options.Value.AuthorizationTimeoutSeconds); await connections.RecheckAsync(c, operation, deadline.Token); }
    internal Task Outcome(object service, Guid app, string action, Guid? tenant, CancellationToken ct)
        => operations.ExecuteAsync(service, _ => Task.CompletedTask, new ServiceOperationExecutionOptions { OperationName = action, AuditAction = action,
            EntityId = app, TenantId = tenant, EntityName = "ManagementCustomerMutation", PermissionEnabled = false, AuditEnabled = true }, ct);
    internal Task<T> Run<T>(object service, Guid app, string action, Func<CancellationToken, Task<T>> run, CancellationToken ct)
        => operations.ExecuteAsync(service, async token => {
            try {
                if (!options.Value.IsValid() || !transport.Value.IsValid()) throw new ManagementCustomerIdentityUnavailableException();
                return await run(token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException("Management mutation request was cancelled.", ct); }
            catch (DiagnosticTokenException) { throw new ManagementCustomerIdentityInvalidRequestException(); }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or
                ManagementIdentityOperationConflictException or ManagementCustomerIdentityPreconditionException or ManagementCustomerIdentityNotFoundException or
                ManagementCustomerIdentityResponseException or ManagementCustomerIdentityRateLimitException or ManagementAuditUnavailableException or
                ManagementCustomerIdentityUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = action, AuditAction = action, EntityId = app,
            EntityName = "ManagementCustomerMutation", PermissionEnabled = false, AuditEnabled = true }, ct);
    internal static bool Uncertain(Exception ex, CancellationToken caller) => !caller.IsCancellationRequested &&
        ex is OperationCanceledException or ManagementCustomerIdentityResponseException or ManagementCustomerIdentityUnavailableException;
}
