using Azure;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Exceptions;
namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed partial class AzureTableTenantInviteStore : IManagedTenantInviteStore
{
    private async Task EnsureTokenIndex(TenantInviteRecord row, CancellationToken ct)
    {
        var entity = ToEntity(row); entity.PartitionKey = TokenPk(row.TokenHash); entity.RowKey = TokenRk();
        try { await IdentityTenantPurgePartition.Add(Table(), entity, _opts.RequireTenantPurgeFencing, ct); }
        catch (RequestFailedException ex) when (ex.Status == 409) {
            var old = await Table().GetEntityAsync<Entities.TenantInviteEntity>(entity.PartitionKey, entity.RowKey, cancellationToken: ct);
            if (old.Value.InviteId != entity.InviteId || old.Value.TenantId != entity.TenantId) throw new IdentityConcurrencyException();
        }
    }
    public async Task<TenantInviteRecord> BeginAcceptanceAsync(Guid tenantId, Guid inviteId, string tokenHash, Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new IdentityConcurrencyException();
        var current = await GetAsync(tenantId, inviteId, ct) ?? throw new IdentityConcurrencyException();
        if (current.TokenHash != tokenHash || current.Status is not (TenantInviteStatuses.Pending or TenantInviteStatuses.Sent) ||
            current.AcceptanceUserId is Guid owner && owner != userId || current.AcceptanceUserId is null && current.ExpiresUtc <= DateTimeOffset.UtcNow)
            throw new IdentityConcurrencyException();
        if (current.AcceptanceUserId == userId) return current;
        var next = current with { AcceptanceId = Guid.NewGuid(), AcceptanceUserId = userId };
        try { await IdentityTenantPurgePartition.Update(Table(), ToEntity(next), new ETag(current.Version!), Azure.Data.Tables.TableUpdateMode.Merge, _opts.RequireTenantPurgeFencing, ct); }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new IdentityConcurrencyException(); }
        return await GetAsync(tenantId, inviteId, ct) ?? throw new IdentityConcurrencyException();
    }
    public async Task<TenantInviteRecord> TransitionAsync(ManagedTenantInviteTransition request, CancellationToken ct = default)
    {
        if (request.TenantId == Guid.Empty || request.InviteId == Guid.Empty || request.OperationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ExpectedVersion) || request.ExpectedVersion == "*" || request.Kind is not ("resend" or "revoke")) throw new IdentityConcurrencyException();
        var current = await GetAsync(request.TenantId, request.InviteId, ct) ?? throw new IdentityConcurrencyException();
        if (current.LastTransitionOperationId == request.OperationId) {
            if (request.Kind == "resend" && current.TokenHash != request.TokenHash || request.Kind == "revoke" && current.Status != TenantInviteStatuses.Revoked)
                throw new IdentityConcurrencyException();
            await EnsureTokenIndex(current, ct); return current;
        }
        if (current.Version != request.ExpectedVersion || current.AcceptanceUserId is not null ||
            current.Status is TenantInviteStatuses.Redeemed or TenantInviteStatuses.Revoked) throw new IdentityConcurrencyException();
        TenantInviteRecord next;
        if (request.Kind == "resend") {
            if (request.TokenHash is not { Length: > 0 and <= 128 } || request.ExpiresUtc <= request.NowUtc || request.ResendCount < 1) throw new IdentityConcurrencyException();
            next = current with { TokenHash = request.TokenHash, ExpiresUtc = request.ExpiresUtc!.Value, LastTransitionOperationId = request.OperationId,
                LastResendUtc = request.NowUtc, ResendWindowUtc = request.ResendWindowUtc, ResendCount = request.ResendCount, Status = TenantInviteStatuses.Pending };
        } else next = current with { Status = TenantInviteStatuses.Revoked, RevokedUtc = request.NowUtc, RevokedByUserId = request.CustomerActorId,
            RevokedReason = request.ExternalActor is null ? null : "support-revoked", LastTransitionOperationId = request.OperationId };
        if (request.ExternalActor is not null) next = next with { RequireVerifiedAcceptance = true };
        var entity = ToEntity(next);
        if (request.ExternalActor is { } actor) {
            entity.IBeamInviteLastManagementOrganizationId = actor.OrganizationId.ToString("D");
            entity.IBeamInviteLastManagementUserId = actor.UserId.ToString("D");
            entity.IBeamInviteLastManagementApplicationId = actor.ApplicationId.ToString("D");
        }
        try { await IdentityTenantPurgePartition.Update(Table(), entity, new ETag(current.Version!), Azure.Data.Tables.TableUpdateMode.Merge, _opts.RequireTenantPurgeFencing, ct); }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new IdentityConcurrencyException(); }
        var changed = await GetAsync(request.TenantId, request.InviteId, ct) ?? throw new IdentityConcurrencyException();
        await EnsureTokenIndex(changed, ct); // The primary hash/status is authoritative if a stale old index remains.
        return changed;
    }
}
