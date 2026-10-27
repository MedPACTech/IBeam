using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Licensing;
using IBeam.Management.Operations;
using IBeam.Services.Abstractions;

namespace IBeam.Management.Licensing;

public sealed class ManagementSeatTargetService(ILicenseSeatAssignmentService native, IManagementOperationAccessService access, IServiceOperationExecutor operations)
{
    public Task<ManagementSeatStatus> ReadAsync(ManagementOperationContext context, CancellationToken ct = default)
        => Run(ManagementSeatOperations.Read, context, async token =>
        {
            var value = await native.ReadManagementSeatsAsync(context.TenantId, context.ResourceId, token);
            await access.RequireAsync(ManagementSeatOperations.Read, context, token);
            if (value.License.TenantId != context.TenantId || value.License.LicenseId != context.ResourceId || !Exact(value.Version) ||
                value.Assignments.Count > LicenseSeatProtocol.MaximumAssignments || value.License.SeatLimit < 0 ||
                value.Assignments.Any(x => x.TenantId != context.TenantId || x.LicenseId != context.ResourceId || x.AssignmentId == Guid.Empty) ||
                value.Assignments.Select(x => x.AssignmentId).Distinct().Count() != value.Assignments.Count) throw new ManagementOperationUnavailableException();
            return new ManagementSeatStatus(context.TenantId, context.ResourceId, value.Version, value.License.SeatLimit, value.Assignments.Count,
                value.Assignments.Select(x => new ManagementSeatAssignment(x.AssignmentId,
                    x.Subject.SubjectType.Equals(LicenseSubjectTypes.User, StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(x.Subject.SubjectId, "D", out var user) && user != Guid.Empty ? user : null)).ToArray());
        }, ct);
    public async Task<ManagementSeatTransitionResult> ChangeAsync(string operation, ManagementOperationContext context, Guid licenseId, Guid userId,
        Guid? assignmentId, string version, CancellationToken ct = default)
    {
        try { return await Run(operation, context, async token =>
        {
            if (operation is not (ManagementSeatOperations.Assign or ManagementSeatOperations.Unassign) || context.OperationId == Guid.Empty || userId == Guid.Empty ||
                licenseId != context.ResourceId || version != context.ResourceVersion || !Exact(version) || operation == ManagementSeatOperations.Assign && assignmentId.HasValue ||
                operation == ManagementSeatOperations.Unassign && (!assignmentId.HasValue || assignmentId.Value == Guid.Empty)) throw new ManagementOperationInvalidException();
            try
            {
                var result = await native.ChangeManagementSeatAsync(new(context.TenantId, licenseId, userId, assignmentId, context.OperationId, Scope(context), version,
                    operation == ManagementSeatOperations.Assign ? "assign" : "unassign"), token);
                await access.RequireAsync(operation, context, token);
                return Project(context, result, operation, userId, assignmentId);
            }
            catch (Exception ex) when (ex is not (LicenseSeatPreconditionException or LicenseSeatConflictException or LicenseSeatCapacityException or
                LicenseSeatUnavailableException or ManagementAccessDeniedException) && (ex is not OperationCanceledException || !ct.IsCancellationRequested))
            {
                using var after = CancellationTokenSource.CreateLinkedTokenSource(ct); after.CancelAfter(TimeSpan.FromSeconds(5));
                await access.RequireAsync(operation, context, after.Token);
                throw new UnknownReply(new(context.TenantId, licenseId, context.OperationId, "unknown", userId, null, null, null));
            }
        }, ct); }
        catch (UnknownReply unknown) { return unknown.Result; }
    }
    public Task<ManagementSeatTransitionResult> FindAsync(string operation, ManagementOperationContext context, CancellationToken ct = default)
        => Run(operation, context, async token =>
        {
            if (operation is not (ManagementSeatOperations.Assign or ManagementSeatOperations.Unassign) || context.OperationId == Guid.Empty) throw new ManagementOperationInvalidException();
            var receipt = await native.FindManagementSeatOperationAsync(context.TenantId, context.ResourceId, context.OperationId, Scope(context),
                operation == ManagementSeatOperations.Assign ? "assign" : "unassign", token);
            await access.RequireAsync(operation, context, token);
            return receipt is null ? new(context.TenantId, context.ResourceId, context.OperationId, "unknown", null, null, null, null)
                : Project(context, receipt, operation, null, null);
        }, ct, lookup: true);
    private static ManagementSeatTransitionResult Project(ManagementOperationContext context, LicenseSeatReceipt value, string operation, Guid? userId, Guid? assignmentId)
    {
        if (value.TenantId != context.TenantId || value.LicenseId != context.ResourceId || value.OperationId != context.OperationId || value.UserId == Guid.Empty ||
            value.AssignmentId == Guid.Empty || userId.HasValue && value.UserId != userId || assignmentId.HasValue && value.AssignmentId != assignmentId ||
            value.Transition != (operation == ManagementSeatOperations.Assign ? "assign" : "unassign") || !Exact(value.Version) ||
            operation == ManagementSeatOperations.Assign && value.Disposition is not ("assigned" or "already-assigned") ||
            operation == ManagementSeatOperations.Unassign && value.Disposition is not ("unassigned" or "not-assigned")) throw new ManagementOperationUnavailableException();
        return new(context.TenantId, context.ResourceId, context.OperationId, "succeeded", value.UserId, value.AssignmentId, value.Disposition, value.Version);
    }
    private static bool Exact(string? value) => value is { Length: > 0 and <= 256 } && value != "*" && !value.Any(char.IsControl);
    private static string Scope(ManagementOperationContext context) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
        context.OrganizationId, context.ApplicationId, context.RealmId, context.TenantId, context.ActorUserId })));
    private sealed class UnknownReply(ManagementSeatTransitionResult result) : Exception("The seat outcome is unknown.") { public ManagementSeatTransitionResult Result { get; } = result; }
    private Task<T> Run<T>(string operation, ManagementOperationContext context, Func<CancellationToken, Task<T>> body, CancellationToken ct, bool lookup = false)
        => operations.ExecuteAsync(this, async token =>
        {
            if (access.ProtocolVersion != 1 || context is null || context.OrganizationId == Guid.Empty || context.ApplicationId == Guid.Empty || context.RealmId == Guid.Empty ||
                context.TenantId == Guid.Empty || context.ResourceId == Guid.Empty || context.ActorUserId == Guid.Empty) throw new ManagementAccessDeniedException();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
            try { await access.RequireAsync(operation, context, timeout.Token); return await body(timeout.Token); }
            catch (LicenseSeatPreconditionException) { throw new ManagementOperationPreconditionException(); }
            catch (Exception ex) when (ex is LicenseSeatConflictException or LicenseSeatCapacityException) { throw new ManagementOperationConflictException(); }
            catch (LicenseSeatUnavailableException) { throw new ManagementOperationUnavailableException(); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ManagementOperationUnavailableException(); }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationInvalidException or ManagementOperationConflictException or
                ManagementOperationPreconditionException or ManagementOperationUnavailableException or ManagementAuditUnavailableException or UnknownReply or OperationCanceledException))
            { throw new ManagementOperationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = lookup ? "management.operations.lookup" : operation,
            EntityName = "ManagementSeat", EntityId = context?.ResourceId, TenantId = context?.TenantId, PermissionEnabled = false, AuditEnabled = true }, ct);
}
public sealed class ManagementSeatReadHandler(ManagementSeatTargetService service) : IManagementOperationHandler<ManagementSeatReadRequest, ManagementSeatStatus>
{
    public Task<ManagementSeatStatus> ExecuteAsync(ManagementOperationContext context, ManagementSeatReadRequest request, CancellationToken ct = default) => service.ReadAsync(context, ct);
}
public sealed class ManagementSeatAssignHandler(ManagementSeatTargetService service) : IManagementOperationHandler<ManagementSeatAssignRequest, ManagementSeatTransitionResult>, IManagementOperationReceiptHandler<ManagementSeatTransitionResult>
{
    public Task<ManagementSeatTransitionResult> ReadReceiptAsync(ManagementOperationContext context, CancellationToken ct = default)
        => service.FindAsync(ManagementSeatOperations.Assign, context, ct);
    public Task<ManagementSeatTransitionResult> ExecuteAsync(ManagementOperationContext context, ManagementSeatAssignRequest request, CancellationToken ct = default)
        => service.ChangeAsync(ManagementSeatOperations.Assign, context, request?.LicenseId ?? Guid.Empty, request?.UserId ?? Guid.Empty, null, request?.ExpectedVersion ?? "", ct);
}
public sealed class ManagementSeatUnassignHandler(ManagementSeatTargetService service) : IManagementOperationHandler<ManagementSeatUnassignRequest, ManagementSeatTransitionResult>, IManagementOperationReceiptHandler<ManagementSeatTransitionResult>
{
    public Task<ManagementSeatTransitionResult> ReadReceiptAsync(ManagementOperationContext context, CancellationToken ct = default)
        => service.FindAsync(ManagementSeatOperations.Unassign, context, ct);
    public Task<ManagementSeatTransitionResult> ExecuteAsync(ManagementOperationContext context, ManagementSeatUnassignRequest request, CancellationToken ct = default)
        => service.ChangeAsync(ManagementSeatOperations.Unassign, context, request?.LicenseId ?? Guid.Empty, request?.UserId ?? Guid.Empty, request?.AssignmentId, request?.ExpectedVersion ?? "", ct);
}
