using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Tenants;

public sealed partial class ManagementTenantAdministrationService(IIdentityTenantAdministrationService identities,
    IManagementTenantAdministrationRepository repository, IManagementIdentityOperationService receipts,
    IManagementCustomerIdentityTargetAccessService access, IOptions<ManagementIdentityOperationOptions> options,
    IServiceOperationExecutor operations) : IManagementTenantAdministrationService, IManagementTenantRenameCapability
{
    public Task<ManagementTenantMutationResult> CreateAsync(CreateManagementCustomerTenant request, string idempotencyKey, CancellationToken ct = default)
        => Run(ManagementCustomerIdentityOperations.TenantsCreate, async (actor, token) => {
            if (request is null) throw new ManagementCustomerIdentityInvalidRequestException();
            var name = Name(request.Name); var reference = Reference(request.ExternalReference);
            var result = await receipts.ExecuteAsync(new() { Operation = actor.Operation, IdempotencyKey = idempotencyKey,
                CanonicalPayload = JsonSerializer.SerializeToUtf8Bytes(new { Name = name, ExternalReference = reference }) },
                new Handler(async (context, cancellation) => {
                    var metadata = await Read(actor, context.ResourceId, cancellation);
                    var tenant = await identities.FindAsync(context.ResourceId, cancellation);
                    if (tenant is null) return metadata is null || metadata.CreateOperationId == context.OperationId
                        ? ManagementIdentityReconciliation.Absent : ManagementIdentityReconciliation.Unknown;
                    if (metadata?.CreateOperationId != context.OperationId) return ManagementIdentityReconciliation.Unknown;
                    if (metadata.Phase == "ready") return ManagementIdentityReconciliation.Applied;
                    if (metadata.LastOperationId != context.OperationId || metadata.Phase != "creating" || tenant.Tenant.Name != name)
                        return ManagementIdentityReconciliation.Unknown;
                    await identities.ResumeCreationAsync(context.ResourceId, Context(actor, context, true), cancellation);
                    await Complete(actor, context, cancellation);
                    return ManagementIdentityReconciliation.Applied;
                }, async (context, cancellation) => {
                    await Begin(actor, context, "creating", reference, cancellation);
                    await access.RequireAsync(actor.Operation, cancellation);
                    await identities.CreateAsync(name, context.ResourceId, Context(actor, context, true), cancellation);
                    await Complete(actor, context, cancellation);
                }), token);
            return await Result(actor, result, token);
        }, ct);

    public Task<ManagementTenantMutationResult> RenameAsync(Guid tenantId, RenameManagementCustomerTenant request, string expectedVersion, string idempotencyKey, CancellationToken ct = default)
        => Run(ManagementCustomerIdentityOperations.TenantsUpdate, async (actor, token) => {
            if (tenantId == Guid.Empty || request is null || string.IsNullOrWhiteSpace(expectedVersion) || expectedVersion is "*" ||
                expectedVersion.Length > 256 || expectedVersion.Any(char.IsControl)) throw new ManagementCustomerIdentityInvalidRequestException();
            if (!identities.SupportsConditionalUpdates) throw new ManagementAccessDeniedException();
            var name = Name(request.Name);
            var result = await receipts.ExecuteAsync(new() { Operation = actor.Operation, IdempotencyKey = idempotencyKey,
                ResourceScope = tenantId, ExistingResourceId = tenantId,
                CanonicalPayload = JsonSerializer.SerializeToUtf8Bytes(new { Name = name, ExpectedVersion = expectedVersion }) },
                new Handler(async (context, cancellation) => {
                    var tenant = await identities.FindAsync(tenantId, cancellation) ?? throw new ManagementCustomerIdentityNotFoundException();
                    var metadata = await Read(actor, tenantId, cancellation);
                    if (tenant.LastOperationId == context.OperationId && tenant.Tenant.Name == name && metadata?.LastOperationId == context.OperationId) {
                        if (metadata.Phase != "ready") {
                            await identities.ResumeRenameAsync(tenantId, Context(actor, context, false), cancellation);
                            await Complete(actor, context, cancellation);
                        }
                        return ManagementIdentityReconciliation.Applied;
                    }
                    if (tenant.Version != expectedVersion) { await AbandonUnapplied(actor, context, cancellation); throw new ManagementCustomerIdentityPreconditionException(); }
                    if (metadata is not null && metadata.Phase != "ready" && metadata.LastOperationId != context.OperationId)
                        throw new ManagementIdentityOperationConflictException();
                    return ManagementIdentityReconciliation.Absent;
                }, async (context, cancellation) => {
                    await Begin(actor, context, "renaming", null, cancellation);
                    await access.RequireAsync(actor.Operation, cancellation);
                    try { await identities.RenameAsync(tenantId, name, expectedVersion, context.OperationId, Context(actor, context, false), cancellation); }
                    catch (IdentityConcurrencyException) { await AbandonUnapplied(actor, context, cancellation); throw new ManagementCustomerIdentityPreconditionException(); }
                    await Complete(actor, context, cancellation);
                }), token);
            return await Result(actor, result, token);
        }, ct);

    private async Task<ManagementTenantMutationResult> Result(ManagementDelegatedActor actor, ManagementIdentityOperationDto receipt, CancellationToken ct)
    {
        ManagementCustomerTenantDto? result = null;
        var metadata = await Read(actor, receipt.ResourceId, ct);
        if (receipt.Status == "succeeded") {
            var row = await identities.FindAsync(receipt.ResourceId, ct) ?? throw new ManagementCustomerIdentityUnavailableException();
            var tenant = row.Tenant;
            var status = string.Equals(tenant.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Active" :
                string.Equals(tenant.Status, "Disabled", StringComparison.OrdinalIgnoreCase) ? "Disabled" : tenant.Status;
            if (tenant.TenantId != receipt.ResourceId || tenant.Name is not { Length: > 0 and <= 512 } || tenant.Name.Any(char.IsControl) ||
                status is not ("Active" or "Disabled") || row.Version?.Length > 256) throw new ManagementCustomerIdentityUnavailableException();
            result = new(tenant.TenantId, tenant.Name, status, tenant.CreatedAt, tenant.UpdatedAt, row.Version);
        }
        await access.RequireAsync(actor.Operation, ct);
        return new(receipt, result, metadata?.Source ?? "unknown", metadata?.ExternalReference);
    }
    private async Task<ManagementTenantAdministrationEntity?> Read(ManagementDelegatedActor actor, Guid tenantId, CancellationToken ct)
    {
        var row = await repository.GetAsync(Scope(actor), tenantId, ct);
        if (row is not null && (row.IdentityRealmId != actor.IdentityRealmId || row.ApplicationKey != actor.ApplicationKey || row.Environment != actor.Environment ||
            row.TenantId != tenantId || row.Source is not ("unknown" or "support-provisioned") || row.Phase is not ("creating" or "renaming" or "status-changing" or "ready") ||
            row.LastOperationId == Guid.Empty || Reference(row.ExternalReference) != row.ExternalReference)) throw new ManagementCustomerIdentityUnavailableException();
        return row;
    }
    private async Task Begin(ManagementDelegatedActor actor, ManagementIdentityMutationContext context, string phase, string? reference, CancellationToken ct)
    {
        var metadata = await Read(actor, context.ResourceId, ct);
        if (metadata is null) {
            var row = new ManagementTenantAdministrationEntity { IdentityRealmId = actor.IdentityRealmId, ApplicationKey = actor.ApplicationKey,
                Environment = actor.Environment, TenantId = context.ResourceId, Source = phase == "creating" ? "support-provisioned" : "unknown",
                ExternalReference = reference, CreateOperationId = phase == "creating" ? context.OperationId : null, LastOperationId = context.OperationId, Phase = phase };
            if (!await repository.TryCreateAsync(row, ct)) {
                metadata = await Read(actor, context.ResourceId, ct);
                if (metadata?.LastOperationId != context.OperationId) throw new ManagementIdentityOperationConflictException();
            }
            return;
        }
        if (metadata.LastOperationId == context.OperationId) return;
        if (phase == "creating" || metadata.Phase != "ready") throw new ManagementIdentityOperationConflictException();
        await repository.ReplaceAsync(metadata with { LastOperationId = context.OperationId, Phase = phase }, metadata.Version, ct);
    }
    private async Task Complete(ManagementDelegatedActor actor, ManagementIdentityMutationContext context, CancellationToken ct)
    {
        var metadata = await Read(actor, context.ResourceId, ct) ?? throw new ManagementCustomerIdentityUnavailableException();
        if (metadata.LastOperationId != context.OperationId) throw new ManagementIdentityOperationConflictException();
        if (metadata.Phase != "ready") await repository.ReplaceAsync(metadata with { Phase = "ready" }, metadata.Version, ct);
    }
    private static ManagementTenantAdministrationScope Scope(ManagementDelegatedActor actor) => new(actor.IdentityRealmId, actor.ApplicationKey, actor.Environment);
    private static TenantExtensionContext Context(ManagementDelegatedActor actor, ManagementIdentityMutationContext context, bool created)
        => new(created ? TenantExtensionOperations.Created : TenantExtensionOperations.Updated, null, actor.CorrelationId, context.OperationId.ToString("D"),
            Metadata: new Dictionary<string, string> { ["origin"] = "external-management", ["operation_id"] = context.OperationId.ToString("D"),
                ["support_organization_id"] = actor.OrganizationId.ToString("D"), ["support_user_id"] = actor.UserId.ToString("D"),
                ["management_application_id"] = actor.ApplicationId.ToString("D") }) { RedactFailureDetails = true };
    private static string Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl)) throw new ManagementCustomerIdentityInvalidRequestException();
        return value.Trim();
    }
    private static string? Reference(string? value)
    {
        if (value is null) return null;
        if (value.Length > 128 || value.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':' or '/')))
            throw new ManagementCustomerIdentityInvalidRequestException();
        return value.Length == 0 ? null : value;
    }
    private Task<ManagementTenantMutationResult> Run(string operation, Func<ManagementDelegatedActor, CancellationToken, Task<ManagementTenantMutationResult>> run, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try {
                if (!options.Value.IsValid()) throw new ManagementCustomerIdentityUnavailableException();
                return await run(await access.RequireAsync(operation, timeout.Token), timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or
                ManagementIdentityOperationConflictException or ManagementCustomerIdentityPreconditionException or ManagementCustomerIdentityNotFoundException or ManagementCustomerIdentityUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = operation, EntityName = "ManagementCustomerTenant",
            PermissionEnabled = false, AuditEnabled = true }, ct);
    private sealed class Handler(Func<ManagementIdentityMutationContext, CancellationToken, Task<ManagementIdentityReconciliation>> reconcile,
        Func<ManagementIdentityMutationContext, CancellationToken, Task> apply) : IManagementIdentityMutationHandler
    {
        public Task<ManagementIdentityReconciliation> ReconcileAsync(ManagementIdentityMutationContext context, CancellationToken ct) => reconcile(context, ct);
        public Task ApplyAsync(ManagementIdentityMutationContext context, CancellationToken ct) => apply(context, ct);
    }
}
