using IBeam.Management.Operations;
using IBeam.Services.Abstractions;

namespace IBeam.Management.Licensing;

public sealed class ManagementSeatPortalService(IManagementOperationClient client, IServiceOperationExecutor operations) : IManagementSeatPortalService
{
    public Task<ManagementSeatStatus> ReadAsync(Guid app, Guid tenant, Guid license, CancellationToken ct = default)
        => Run(app, tenant, license, ManagementSeatOperations.Read, async token =>
        {
            var value = await client.ExecuteAsync<ManagementSeatReadRequest, ManagementSeatStatus>(app, ManagementSeatOperations.Read, tenant, license, new(), "", null, token);
            if (value is null || value.TenantId != tenant || value.LicenseId != license || !Exact(value.Version) || value.SeatLimit < 0 ||
                value.Assignments is null || value.Assignments.Length > 2000 || value.AssignedCount != value.Assignments.Length ||
                value.Assignments.Any(x => x is null || x.AssignmentId == Guid.Empty || x.UserId == Guid.Empty) ||
                value.Assignments.Select(x => x.AssignmentId).Distinct().Count() != value.Assignments.Length) throw new ManagementOperationUnavailableException();
            return new ManagementSeatStatus(tenant, license, value.Version, value.SeatLimit, value.AssignedCount,
                value.Assignments.Select(x => new ManagementSeatAssignment(x.AssignmentId, x.UserId)).ToArray());
        }, ct);
    public Task<ManagementSeatTransitionResult> AssignAsync(Guid app, Guid tenant, ManagementSeatAssignRequest request, string key, CancellationToken ct = default)
        => Change(app, tenant, request?.LicenseId ?? Guid.Empty, request?.UserId ?? Guid.Empty, null, ManagementSeatOperations.Assign, request!, request?.ExpectedVersion, key, ct);
    public Task<ManagementSeatTransitionResult> UnassignAsync(Guid app, Guid tenant, ManagementSeatUnassignRequest request, string key, CancellationToken ct = default)
        => Change(app, tenant, request?.LicenseId ?? Guid.Empty, request?.UserId ?? Guid.Empty, request?.AssignmentId, ManagementSeatOperations.Unassign, request!, request?.ExpectedVersion, key, ct);
    private Task<ManagementSeatTransitionResult> Change<T>(Guid app, Guid tenant, Guid license, Guid user, Guid? assignment, string operation, T request, string? version, string key, CancellationToken ct)
        => Run(app, tenant, license, operation, async token =>
        {
            if (request is null || user == Guid.Empty || operation == ManagementSeatOperations.Unassign && (!assignment.HasValue || assignment.Value == Guid.Empty) ||
                !Exact(version) || !IBeam.Management.Services.CustomerIdentity.Delegation.ManagementDelegationBinding.ValidIdempotencyKey(key)) throw new ManagementOperationInvalidException();
            try { return Project(tenant, license, operation, await client.ExecuteAsync<T, ManagementSeatTransitionResult>(app, operation, tenant, license, request, key, version, token), user, assignment); }
            catch (ManagementOperationUncertainException unknown)
            {
                if (unknown.ApplicationId != app || unknown.TenantId != tenant || unknown.ResourceId != license || unknown.OperationId == Guid.Empty) throw new ManagementOperationUnavailableException();
                return new ManagementSeatTransitionResult(tenant, license, unknown.OperationId, "unknown", user, null, null, null);
            }
        }, ct);
    public Task<ManagementSeatTransitionResult> FindAsync(Guid app, Guid tenant, Guid license, Guid id, string operation, CancellationToken ct = default)
        => Run(app, tenant, license, operation, async token =>
        {
            if (operation is not (ManagementSeatOperations.Assign or ManagementSeatOperations.Unassign) || id == Guid.Empty) throw new ManagementOperationInvalidException();
            var value = Project(tenant, license, operation, await client.ReadReceiptAsync<ManagementSeatTransitionResult>(app, operation, tenant, license, id, token), null, null);
            if (value.OperationId != id) throw new ManagementOperationUnavailableException(); return value;
        }, ct, lookup: true);
    private static ManagementSeatTransitionResult Project(Guid tenant, Guid license, string operation, ManagementSeatTransitionResult value, Guid? user, Guid? assignment)
    {
        if (value is null || value.TenantId != tenant || value.LicenseId != license || value.OperationId == Guid.Empty || value.Outcome is not ("succeeded" or "unknown") ||
            user.HasValue && value.UserId != user || value.Outcome == "unknown" && (value.AssignmentId.HasValue || value.Disposition is not null || value.Version is not null) ||
            value.Outcome == "succeeded" && (!value.UserId.HasValue || value.UserId == Guid.Empty || !value.AssignmentId.HasValue || value.AssignmentId == Guid.Empty || !Exact(value.Version) ||
                assignment.HasValue && value.AssignmentId != assignment ||
                operation == ManagementSeatOperations.Assign && value.Disposition is not ("assigned" or "already-assigned") ||
                operation == ManagementSeatOperations.Unassign && value.Disposition is not ("unassigned" or "not-assigned"))) throw new ManagementOperationUnavailableException();
        return new(tenant, license, value.OperationId, value.Outcome, value.UserId, value.AssignmentId, value.Disposition, value.Version);
    }
    private static bool Exact(string? value) => value is { Length: > 0 and <= 256 } && value != "*" && !value.Any(char.IsControl);
    private sealed class UnknownReply(ManagementSeatTransitionResult result) : Exception("The seat outcome is unknown.") { public ManagementSeatTransitionResult Result { get; } = result; }
    private async Task<T> Run<T>(Guid app, Guid tenant, Guid license, string operation, Func<CancellationToken, Task<T>> body, CancellationToken ct, bool lookup = false)
    {
        try { return await operations.ExecuteAsync(this, async token =>
        {
            if (app == Guid.Empty || tenant == Guid.Empty || license == Guid.Empty) throw new ManagementOperationInvalidException();
            var value = await body(token); if (value is ManagementSeatTransitionResult { Outcome: "unknown" } unknown) throw new UnknownReply(unknown); return value;
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = lookup ? "management.operations.lookup" : operation,
            EntityName = "ManagementSeatPortal", EntityId = app, TenantId = tenant, PermissionEnabled = false, AuditEnabled = true }, ct); }
        catch (UnknownReply unknown) { return (T)(object)unknown.Result; }
        catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationInvalidException or ManagementOperationPreconditionException or
            ManagementOperationConflictException or ManagementOperationUnavailableException or ManagementAuditUnavailableException or OperationCanceledException)) { throw new ManagementOperationUnavailableException(); }
    }
}
