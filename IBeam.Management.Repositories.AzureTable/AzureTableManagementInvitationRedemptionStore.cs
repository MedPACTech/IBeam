using Azure;
using Azure.Data.Tables;
namespace IBeam.Management.Repositories.AzureTable;

public sealed class AzureTableManagementInvitationRedemptionStore(TableClient managementIdentityInvites) : IManagementInvitationRedemptionStore
{
    public async Task<bool> TryClaimAsync(Guid organizationId, Guid inviteId, Guid userId, CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty || inviteId == Guid.Empty || userId == Guid.Empty) throw new ArgumentException("Invitation identifiers are required.");
        try
        {
            await managementIdentityInvites.AddEntityAsync(new TableEntity("MGTREDEEM|" + organizationId.ToString("N"), inviteId.ToString("N"))
                { ["UserId"] = userId, ["ClaimedUtc"] = DateTimeOffset.UtcNow }, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }
    }
}
