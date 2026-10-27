using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Licensing;
using IBeam.Management.Operations;
using IBeam.Services.Abstractions;

namespace IBeam.Management.Licensing;

/// <summary>Optional typed target adapter. A verified live Management boundary is mandatory before native reads or writes.</summary>
public sealed class ManagementLicenseTargetService(ITenantLicenseService native, IManagementOperationAccessService access,
    IServiceOperationExecutor operations)
{
    public Task<ManagementLicenseStatus> ReadAsync(ManagementOperationContext context, CancellationToken ct = default)
        => Run(ManagementLicenseOperations.Read, context, async token =>
        {
            var snapshot = await native.ReadStatusAsync(context.TenantId, context.ResourceId, token);
            await access.RequireAsync(ManagementLicenseOperations.Read, context, token);
            if (snapshot.License.TenantId != context.TenantId || snapshot.License.LicenseId != context.ResourceId ||
                !IBeam.Licensing.Services.TenantLicenseStatusAdministrationService.Exact(snapshot.Version)) throw new ManagementOperationUnavailableException();
            return new ManagementLicenseStatus(context.TenantId, context.ResourceId, SafeStatus(snapshot.License.Status), snapshot.Version);
        }, ct);
    public Task<ManagementLicenseTransitionResult> ChangeAsync(string operation, ManagementOperationContext context,
        Guid licenseId, string version, CancellationToken ct = default)
        => RunChange(operation, context, async token =>
        {
            if (operation is not (ManagementLicenseOperations.Suspend or ManagementLicenseOperations.Restore) ||
                context.OperationId == Guid.Empty || licenseId != context.ResourceId || version != context.ResourceVersion ||
                !IBeam.Licensing.Services.TenantLicenseStatusAdministrationService.Exact(version)) throw new ManagementOperationInvalidException();
            try
            {
                var result = await native.ChangeStatusAsync(new(context.TenantId, licenseId, context.OperationId, Scope(context), version,
                    operation == ManagementLicenseOperations.Suspend ? "suspend" : "restore"), token);
                await access.RequireAsync(operation, context, token);
                return Project(context, result, operation);
            }
            catch (Exception ex) when (ex is not (LicenseStatusPreconditionException or LicenseStatusConflictException or
                LicenseStatusUnavailableException or ManagementAccessDeniedException or ManagementOperationInvalidException) &&
                (ex is not OperationCanceledException || !ct.IsCancellationRequested))
            {
                // A provider failure can follow commit. Never retry or claim failure/success from an unknown reply.
                using var after = CancellationTokenSource.CreateLinkedTokenSource(ct); after.CancelAfter(TimeSpan.FromSeconds(5));
                await access.RequireAsync(operation, context, after.Token);
                return new ManagementLicenseTransitionResult(context.TenantId, licenseId, context.OperationId, "unknown", null, null);
            }
        }, ct);
    private async Task<ManagementLicenseTransitionResult> RunChange(string operation, ManagementOperationContext context,
        Func<CancellationToken, Task<ManagementLicenseTransitionResult>> body, CancellationToken ct)
    {
        try { return await Run(operation, context, async token =>
        {
            var value = await body(token);
            if (value.Outcome == "unknown") throw new UnknownReply(value);
            return value;
        }, ct); }
        catch (UnknownReply unknown) { return unknown.Result; }
    }
    // The executor records a failed/uncertain attempt, never a successful transition from an unknown reply.
    private sealed class UnknownReply(ManagementLicenseTransitionResult result) : Exception("The licence outcome is unknown.")
    { public ManagementLicenseTransitionResult Result { get; } = result; }
    public Task<ManagementLicenseTransitionResult> FindAsync(string operation, ManagementOperationContext context, CancellationToken ct = default)
        => Run(operation, context, async token =>
        {
            if (operation is not (ManagementLicenseOperations.Suspend or ManagementLicenseOperations.Restore) || context.OperationId == Guid.Empty)
                throw new ManagementOperationInvalidException();
            var receipt = await native.FindStatusOperationAsync(context.TenantId, context.ResourceId, context.OperationId, Scope(context),
                operation == ManagementLicenseOperations.Suspend ? "suspend" : "restore", token);
            await access.RequireAsync(operation, context, token);
            return receipt is null ? new(context.TenantId, context.ResourceId, context.OperationId, "unknown", null, null) : Project(context, receipt, operation);
        }, ct, lookup: true);
    private static string Scope(ManagementOperationContext context) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
        context.OrganizationId, context.ApplicationId, context.RealmId, context.TenantId, context.ActorUserId })));
    private static ManagementLicenseTransitionResult Project(ManagementOperationContext context, LicenseStatusReceipt receipt, string operation)
    {
        if (receipt.TenantId != context.TenantId || receipt.LicenseId != context.ResourceId || receipt.OperationId != context.OperationId ||
            receipt.Transition != (operation == ManagementLicenseOperations.Suspend ? "suspend" : "restore") ||
            operation == ManagementLicenseOperations.Suspend && receipt.Status != LicenseStatuses.Suspended ||
            operation == ManagementLicenseOperations.Restore && receipt.Status is not (LicenseStatuses.Active or LicenseStatuses.Trialing or LicenseStatuses.Grace or LicenseStatuses.Manual) ||
            !IBeam.Licensing.Services.TenantLicenseStatusAdministrationService.Exact(receipt.Version)) throw new ManagementOperationUnavailableException();
        return new(context.TenantId, context.ResourceId, context.OperationId, "succeeded", SafeStatus(receipt.Status), receipt.Version);
    }
    private static string SafeStatus(string status) => status is LicenseStatuses.Active or LicenseStatuses.Trialing or LicenseStatuses.Manual or
        LicenseStatuses.Grace or LicenseStatuses.Suspended or LicenseStatuses.Revoked or LicenseStatuses.Expired ? status : "unknown";
    private Task<T> Run<T>(string operation, ManagementOperationContext context, Func<CancellationToken, Task<T>> body, CancellationToken ct, bool lookup = false)
        => operations.ExecuteAsync(this, async token =>
        {
            if (access.ProtocolVersion != 1 || context is null || context.OrganizationId == Guid.Empty || context.ApplicationId == Guid.Empty ||
                context.RealmId == Guid.Empty || context.TenantId == Guid.Empty || context.ActorUserId == Guid.Empty || context.ResourceId == Guid.Empty)
                throw new ManagementAccessDeniedException();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
            try { await access.RequireAsync(operation, context, timeout.Token); return await body(timeout.Token); }
            catch (LicenseStatusPreconditionException) { throw new ManagementOperationPreconditionException(); }
            catch (LicenseStatusConflictException) { throw new ManagementOperationConflictException(); }
            catch (LicenseStatusUnavailableException) { throw new ManagementOperationUnavailableException(); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ManagementOperationUnavailableException(); }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationInvalidException or
                ManagementOperationConflictException or ManagementOperationPreconditionException or ManagementOperationUnavailableException or
                ManagementAuditUnavailableException or UnknownReply or OperationCanceledException)) { throw new ManagementOperationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = lookup ? "management.operations.lookup" : operation, EntityName = "ManagementLicense",
            EntityId = context?.ResourceId, TenantId = context?.TenantId, PermissionEnabled = false, AuditEnabled = true }, ct);
}
public sealed class ManagementLicenseReadHandler(ManagementLicenseTargetService service) : IManagementOperationHandler<ManagementLicenseReadRequest, ManagementLicenseStatus>
{
    public Task<ManagementLicenseStatus> ExecuteAsync(ManagementOperationContext context, ManagementLicenseReadRequest request, CancellationToken ct = default)
        => service.ReadAsync(context, ct);
}
public sealed class ManagementLicenseSuspendHandler(ManagementLicenseTargetService service) : IManagementOperationHandler<ManagementLicenseSuspendRequest, ManagementLicenseTransitionResult>
{
    public Task<ManagementLicenseTransitionResult> ExecuteAsync(ManagementOperationContext context, ManagementLicenseSuspendRequest request, CancellationToken ct = default)
        => service.ChangeAsync(ManagementLicenseOperations.Suspend, context, request?.LicenseId ?? Guid.Empty, request?.ExpectedVersion ?? "", ct);
}
public sealed class ManagementLicenseRestoreHandler(ManagementLicenseTargetService service) : IManagementOperationHandler<ManagementLicenseRestoreRequest, ManagementLicenseTransitionResult>
{
    public Task<ManagementLicenseTransitionResult> ExecuteAsync(ManagementOperationContext context, ManagementLicenseRestoreRequest request, CancellationToken ct = default)
        => service.ChangeAsync(ManagementLicenseOperations.Restore, context, request?.LicenseId ?? Guid.Empty, request?.ExpectedVersion ?? "", ct);
}
