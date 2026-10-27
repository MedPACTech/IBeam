using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Identity.Target.Services.Tenants;
public sealed partial class ManagementTenantAdministrationService
{
    public Task<ManagementTenantMutationResult> SetStatusAsync(Guid tenant, bool active, string version, string key, CancellationToken ct = default)
        => Run(ManagementCustomerIdentityOperations.TenantsStatus, async (actor, token) => {
            if (tenant == Guid.Empty || string.IsNullOrWhiteSpace(version) || version == "*" || version.Length > 256 || version.Any(char.IsControl))
                throw new ManagementCustomerIdentityInvalidRequestException();
            if (identities is not IIdentityTenantStatusAdministrationService { SupportsConditionalStatus: true } status) throw new ManagementAccessDeniedException();
            var desired = active ? IdentityTenantStatuses.Active : IdentityTenantStatuses.Disabled;
            var receipt = await receipts.ExecuteAsync(new() { Operation = actor.Operation, ResourceScope = tenant, ExistingResourceId = tenant, IdempotencyKey = key,
                CanonicalPayload = JsonSerializer.SerializeToUtf8Bytes(new { IsActive = active, ExpectedVersion = version }) },
                new Handler(async (context, cancel) => {
                    var current = await identities.FindAsync(tenant, cancel) ?? throw new ManagementCustomerIdentityNotFoundException();
                    var metadata = await Read(actor, tenant, cancel);
                    if (current.LastOperationId == context.OperationId && current.Tenant.Status == desired && metadata?.LastOperationId == context.OperationId) {
                        if (metadata.Phase != "ready") { await status.ResumeStatusAsync(tenant, active, StatusContext(actor, context, active), cancel); await Complete(actor, context, cancel); }
                        return ManagementIdentityReconciliation.Applied;
                    }
                    if (current.Version != version) { await AbandonUnapplied(actor, context, cancel); throw new ManagementCustomerIdentityPreconditionException(); }
                    if (metadata is not null && metadata.Phase != "ready" && metadata.LastOperationId != context.OperationId) throw new ManagementIdentityOperationConflictException();
                    return ManagementIdentityReconciliation.Absent;
                }, async (context, cancel) => {
                    await Begin(actor, context, "status-changing", null, cancel); await access.RequireAsync(actor.Operation, cancel);
                    try { await status.SetStatusAsync(tenant, active, version, context.OperationId, StatusContext(actor, context, active), cancel); }
                    catch (IdentityConcurrencyException) { await AbandonUnapplied(actor, context, cancel); throw new ManagementCustomerIdentityPreconditionException(); }
                    await Complete(actor, context, cancel);
                }), token);
            return await Result(actor, receipt, token);
        }, ct);
    private async Task AbandonUnapplied(ManagementDelegatedActor actor, ManagementIdentityMutationContext context, CancellationToken ct)
    {
        var current = await identities.FindAsync(context.ResourceId, ct) ?? throw new ManagementCustomerIdentityNotFoundException();
        var row = await Read(actor, context.ResourceId, ct);
        if (current.LastOperationId != context.OperationId && row?.LastOperationId == context.OperationId && row.Phase is "renaming" or "status-changing")
            await repository.ReplaceAsync(row with { Phase = "ready" }, row.Version, ct);
    }
    private static TenantExtensionContext StatusContext(ManagementDelegatedActor actor, ManagementIdentityMutationContext context, bool active)
        => Context(actor, context, false) with { Operation = active ? TenantExtensionOperations.Activated : TenantExtensionOperations.Deactivated, RequireLifecycleDelivery = true };
}
