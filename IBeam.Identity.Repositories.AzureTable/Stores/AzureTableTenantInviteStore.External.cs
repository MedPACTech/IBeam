using Azure;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Exceptions;
namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed partial class AzureTableTenantInviteStore : IExternalTenantInviteStore
{
    public async Task<TenantInviteRecord> EnsureAsync(TenantInviteRecord invite, CancellationToken ct = default)
    {
        // The primary insert fences expired workers. Index repair is safe after a crash between partitions.
        try { await IdentityTenantPurgePartition.Add(Table(), ToEntity(invite), _opts.RequireTenantPurgeFencing, ct); }
        catch (RequestFailedException ex) when (ex.Status == 409) { }
        var current = await GetAsync(invite.TenantId, invite.InviteId, ct)
            ?? throw new IdentityValidationException("External invitation is unavailable.");
        if (current.InvitedByUserId != Guid.Empty || current.CausationId != invite.CausationId ||
            current.TokenHash != invite.TokenHash || current.NormalizedDestination != invite.NormalizedDestination ||
            current.DestinationType != invite.DestinationType || current.CreatedUtc != invite.CreatedUtc ||
            current.ExpiresUtc != invite.ExpiresUtc || !current.RequirePasswordSetup ||
            current.SetAsDefaultTenant || (current.RoleNames?.Count ?? 0) != 0 || (current.AccessGrants?.Count ?? 0) != 0 || current.RedirectUrl is not null ||
            !(current.RoleIds ?? []).Order().SequenceEqual((invite.RoleIds ?? []).Order()) ||
            Serialize(current.Metadata) != Serialize(invite.Metadata)) throw new IdentityValidationException("External invitation conflicts.");
        var index = ToEntity(current); index.PartitionKey = TokenPk(current.TokenHash); index.RowKey = TokenRk();
        try { await IdentityTenantPurgePartition.Add(Table(), index, _opts.RequireTenantPurgeFencing, ct); }
        catch (RequestFailedException ex) when (ex.Status == 409) {
            var existing = await Table().GetEntityAsync<Entities.TenantInviteEntity>(index.PartitionKey, index.RowKey, cancellationToken: ct);
            if (existing.Value.TenantId != index.TenantId || existing.Value.InviteId != index.InviteId)
                throw new IdentityValidationException("External invitation index conflicts.");
        }
        return current;
    }
}
