using IBeam.Management.Operations;
using IBeam.Services.Abstractions;

namespace IBeam.Management.Licensing;

/// <summary>Typed calls only; the registered client owns live authority, exact route binding, deadlines and safe tracking.</summary>
public sealed class ManagementLicensePortalService(IManagementOperationClient client, IServiceOperationExecutor operations) : IManagementLicensePortalService
{
    public Task<ManagementLicenseStatus> ReadAsync(Guid app, Guid tenant, Guid license, CancellationToken ct = default)
        => Run(app, tenant, license, ManagementLicenseOperations.Read, async token =>
        {
            var result = await client.ExecuteAsync<ManagementLicenseReadRequest, ManagementLicenseStatus>(app, ManagementLicenseOperations.Read, tenant, license, new(), "", null, token);
            if (result is null || result.TenantId != tenant || result.LicenseId != license || !Exact(result.Version) || !Status(result.Status)) throw new ManagementOperationUnavailableException();
            return new ManagementLicenseStatus(tenant, license, result.Status, result.Version);
        }, ct);
    public Task<ManagementLicenseTransitionResult> SuspendAsync(Guid app, Guid tenant, ManagementLicenseSuspendRequest request, string key, CancellationToken ct = default)
        => Change(app, tenant, request?.LicenseId ?? Guid.Empty, ManagementLicenseOperations.Suspend, request!, request?.ExpectedVersion, key, ct);
    public Task<ManagementLicenseTransitionResult> RestoreAsync(Guid app, Guid tenant, ManagementLicenseRestoreRequest request, string key, CancellationToken ct = default)
        => Change(app, tenant, request?.LicenseId ?? Guid.Empty, ManagementLicenseOperations.Restore, request!, request?.ExpectedVersion, key, ct);
    private Task<ManagementLicenseTransitionResult> Change<T>(Guid app, Guid tenant, Guid license, string operation, T request, string? version, string key, CancellationToken ct)
        => Run(app, tenant, license, operation, async token =>
        {
            if (request is null || !Exact(version) || !IBeam.Management.Services.CustomerIdentity.Delegation.ManagementDelegationBinding.ValidIdempotencyKey(key))
                throw new ManagementOperationInvalidException();
            try { return Project(tenant, license, await client.ExecuteAsync<T, ManagementLicenseTransitionResult>(app, operation, tenant, license, request, key, version, token), operation); }
            catch (ManagementOperationUncertainException uncertain)
            {
                if (uncertain.ApplicationId != app || uncertain.TenantId != tenant || uncertain.ResourceId != license || uncertain.OperationId == Guid.Empty)
                    throw new ManagementOperationUnavailableException();
                return new ManagementLicenseTransitionResult(tenant, license, uncertain.OperationId, "unknown", null, null);
            }
        }, ct);
    public Task<ManagementLicenseTransitionResult> FindAsync(Guid app, Guid tenant, Guid license, Guid id, string operation, CancellationToken ct = default)
        => Run(app, tenant, license, operation, async token =>
        {
            if (operation is not (ManagementLicenseOperations.Suspend or ManagementLicenseOperations.Restore) || id == Guid.Empty) throw new ManagementOperationInvalidException();
            var result = Project(tenant, license, await client.ReadReceiptAsync<ManagementLicenseTransitionResult>(app, operation, tenant, license, id, token), operation);
            if (result.OperationId != id) throw new ManagementOperationUnavailableException();
            return result;
        }, ct, lookup: true);
    private static ManagementLicenseTransitionResult Project(Guid tenant, Guid license, ManagementLicenseTransitionResult value, string operation)
    {
        if (value is null || value.TenantId != tenant || value.LicenseId != license || value.OperationId == Guid.Empty ||
            value.Outcome is not ("succeeded" or "unknown") || value.Outcome == "succeeded" && (!Status(value.Status) || !Exact(value.Version)) ||
            value.Outcome == "succeeded" && operation == ManagementLicenseOperations.Suspend && value.Status != "suspended" ||
            value.Outcome == "succeeded" && operation == ManagementLicenseOperations.Restore && value.Status is not ("active" or "trialing" or "grace" or "manual") ||
            value.Outcome == "unknown" && (value.Status is not null || value.Version is not null)) throw new ManagementOperationUnavailableException();
        return new(tenant, license, value.OperationId, value.Outcome, value.Status, value.Version);
    }
    private static bool Exact(string? value) => value is { Length: > 0 and <= 256 } && value != "*" && !value.Any(char.IsControl);
    private static bool Status(string? value) => value is "active" or "trialing" or "manual" or "grace" or "suspended" or "revoked" or "expired" or "unknown";
    private async Task<T> Run<T>(Guid app, Guid tenant, Guid license, string operation, Func<CancellationToken, Task<T>> body, CancellationToken ct, bool lookup = false)
    {
        try { return await operations.ExecuteAsync(this, async token =>
        {
            if (app == Guid.Empty || tenant == Guid.Empty || license == Guid.Empty) throw new ManagementOperationInvalidException();
            var value = await body(token);
            if (value is ManagementLicenseTransitionResult { Outcome: "unknown" } unknown) throw new UnknownReply(unknown);
            return value;
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = lookup ? "management.operations.lookup" : operation, EntityName = "ManagementLicensePortal",
            EntityId = app, TenantId = tenant, PermissionEnabled = false, AuditEnabled = true }, ct); }
        catch (UnknownReply unknown) { return (T)(object)unknown.Result; }
        catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationInvalidException or ManagementOperationPreconditionException or
            ManagementOperationConflictException or ManagementOperationUnavailableException or ManagementAuditUnavailableException or OperationCanceledException))
        { throw new ManagementOperationUnavailableException(); }
    }
    private sealed class UnknownReply(ManagementLicenseTransitionResult result) : Exception("The licence outcome is unknown.")
    { public ManagementLicenseTransitionResult Result { get; } = result; }
}
