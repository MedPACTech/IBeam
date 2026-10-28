using IBeam.Identity.Events;
using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Services.Abstractions;
namespace IBeam.Management.Identity.Target.Services.Ownership;

public sealed class ManagementCustomerOwnershipService : IManagementCustomerOwnershipService
{
    private readonly IIdentityTenantOwnershipAdministrationService native;
    private readonly IIdentityTenantOwnershipTransferStore store;
    private readonly IManagementIdentityOperationService receipts;
    private readonly IManagementCustomerIdentityTargetAccessService access;
    private readonly IServiceOperationExecutor operations;
    public int ProtocolVersion => 1;
    public ManagementCustomerOwnershipService(IIdentityTenantOwnershipAdministrationService native, IIdentityTenantMembershipConditionalStore memberships,
        IIdentityTenantAccessEnforcement enforcement, IManagementIdentityOperationService receipts,
        IManagementCustomerIdentityTargetAccessService access, IServiceOperationExecutor operations)
    {
        if (memberships is not IIdentityTenantOwnershipTransferStore { OwnershipProtocolVersion: 1, RequiresOwnershipProtection: true } atomic ||
            enforcement.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(enforcement.Mode) || enforcement.Mode.Length > 100)
            throw new InvalidOperationException("Atomic ownership storage and live customer request/refresh enforcement are required.");
        this.native = native; store = atomic; this.receipts = receipts; this.access = access; this.operations = operations;
    }
    public Task<ManagementCustomerOwnershipDto> ReadAsync(Guid tenant, CancellationToken ct = default)
        => Run(tenant, null, ManagementCustomerIdentityOperations.OwnersRead, async token => {
            var value = await native.ReadOwnersAsync(tenant, token);
            if (value.TenantId != tenant || !TransferManagementCustomerOwnership.Exact(value.Version) || value.Owners.Count > 5000 ||
                value.Owners.Any(x => x.UserId == Guid.Empty || !TransferManagementCustomerOwnership.Exact(x.MembershipVersion)) ||
                value.Owners.Select(x => x.UserId).Distinct().Count() != value.Owners.Count) throw new ManagementCustomerIdentityUnavailableException();
            await access.RequireAsync(ManagementCustomerIdentityOperations.OwnersRead, token);
            return new ManagementCustomerOwnershipDto(tenant, value.Version, value.Owners.Select(x => new ManagementCustomerOwnerDto(x.UserId, x.MembershipVersion)).ToArray());
        }, ct);
    public Task<ManagementCustomerOwnershipResult> TransferAsync(Guid tenant, TransferManagementCustomerOwnership request, string version, string key, CancellationToken ct = default)
        => Run(tenant, request?.SourceUserId, ManagementCustomerIdentityOperations.OwnersTransfer, async outer =>
            await operations.ExecuteAsync(this, async token => {
            if (request is null || !request.IsValid() || !TransferManagementCustomerOwnership.Exact(version)) throw new ManagementCustomerIdentityInvalidRequestException();
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { request.SourceUserId, request.DestinationUserId,
                request.SourceVersion, request.DestinationVersion, OwnershipVersion = version });
            IdentityTenantOwnershipTransferRequest Command(Guid operation) => new(tenant, request.SourceUserId, request.DestinationUserId,
                request.SourceVersion, request.DestinationVersion, version, operation);
            async Task ApplyNative(Guid operation, CancellationToken inner) {
                try { await native.TransferAsync(Command(operation), inner); }
                catch (IdentityConcurrencyException) { throw new ManagementCustomerIdentityPreconditionException(); }
                catch (IdentityNotFoundException) { throw new ManagementCustomerIdentityNotFoundException(); }
                catch (IdentityValidationException) { throw new ManagementIdentityOperationConflictException(); }
            }
            var receipt = await receipts.ExecuteAsync(new() { Operation = ManagementCustomerIdentityOperations.OwnersTransfer,
                ResourceScope = tenant, ExistingResourceId = tenant, IdempotencyKey = key, CanonicalPayload = payload },
                new Handler(async (context, inner) => {
                    var existing = await store.FindOwnershipTransferAsync(tenant, context.OperationId, inner);
                    if (existing is null) return ManagementIdentityReconciliation.Absent;
                    // This is an explicit same-intent POST retry, never an operation poll.
                    await ApplyNative(context.OperationId, inner);
                    return ManagementIdentityReconciliation.Applied;
                }, async (context, inner) => {
                    await access.RequireAsync(ManagementCustomerIdentityOperations.OwnersTransfer, inner);
                    await ApplyNative(context.OperationId, inner);
                }), token);
            await access.RequireAsync(ManagementCustomerIdentityOperations.OwnersTransfer, token);
            // The outer action audits source + outcome; a separate metadata-only entry records destination + outcome.
            await operations.ExecuteAsync(this, _ => Task.CompletedTask, new ServiceOperationExecutionOptions {
                OperationName = ManagementCustomerIdentityOperations.OwnersTransfer + ".destination." + receipt.Status,
                AuditAction = ManagementCustomerIdentityOperations.OwnersTransfer + ".destination." + receipt.Status,
                EntityName = "ManagementOwnershipDestination", EntityId = request.DestinationUserId, TenantId = tenant,
                AuditEnabled = true, PermissionEnabled = false }, token);
            return new ManagementCustomerOwnershipResult(receipt);
        }, new ServiceOperationExecutionOptions { OperationName = ManagementCustomerIdentityOperations.OwnersTransfer + ".destination",
            AuditAction = ManagementCustomerIdentityOperations.OwnersTransfer + ".destination", EntityName = "ManagementOwnershipDestination",
            EntityId = request?.DestinationUserId, TenantId = tenant, PermissionEnabled = false, AuditEnabled = true }, outer), ct);
    private Task<T> Run<T>(Guid tenant, Guid? source, string authority, Func<CancellationToken, Task<T>> body, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
            try {
                var eventActor = await access.RequireAsync(authority, timeout.Token);
                using var eventCapture = IdentityMembershipEventActorContext.Push(new(IdentityMembershipActorKind.Support,
                    eventActor.UserId, eventActor.OrganizationId, eventActor.ApplicationId,
                    Guid.TryParse(eventActor.CorrelationId, out var eventCorrelation) ? eventCorrelation : null));
                if (tenant == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
                return await body(timeout.Token);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (IdentityConcurrencyException) { throw new ManagementCustomerIdentityPreconditionException(); }
            catch (IdentityNotFoundException) { throw new ManagementCustomerIdentityNotFoundException(); }
            catch (IdentityValidationException) { throw new ManagementIdentityOperationConflictException(); }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or
                ManagementCustomerIdentityNotFoundException or ManagementIdentityOperationConflictException or ManagementCustomerIdentityPreconditionException or
                ManagementCustomerIdentityUnavailableException or ManagementAuditUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = authority, AuditAction = authority, EntityName = "ManagementOwnershipSource",
            TenantId = tenant, EntityId = source, PermissionEnabled = false, AuditEnabled = true }, ct);
    private sealed class Handler(Func<ManagementIdentityMutationContext, CancellationToken, Task<ManagementIdentityReconciliation>> reconcile,
        Func<ManagementIdentityMutationContext, CancellationToken, Task> apply) : IManagementIdentityMutationHandler
    {
        public Task<ManagementIdentityReconciliation> ReconcileAsync(ManagementIdentityMutationContext context, CancellationToken ct) => reconcile(context, ct);
        public Task ApplyAsync(ManagementIdentityMutationContext context, CancellationToken ct) => apply(context, ct);
    }
}
