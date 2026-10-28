using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableOAuthConsentStore : IOAuthConsentStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _options;

    public AzureTableOAuthConsentStore(
        TableServiceClient serviceClient,
        IOptions<AzureTableIdentityOptions> options)
    {
        _serviceClient = serviceClient;
        _options = options.Value;
    }

    public async Task<OAuthConsentRecord?> GetAsync(
        Guid userId,
        Guid tenantId,
        string clientId,
        string resource,
        CancellationToken ct = default)
    {
        try
        {
            if (await IdentityTenantPurgePartition.HasTenantFence(_serviceClient, _options, tenantId, ct) || await IdentityTenantPurgePartition.IsFenced(Table(), tenantId, _options.OAuthConsentsPk(tenantId, userId), ct)) return null;
            var response = await Table().GetEntityIfExistsAsync<OAuthConsentEntity>(
                _options.OAuthConsentsPk(tenantId, userId),
                _options.OAuthConsentsRk(clientId, resource),
                cancellationToken: ct).ConfigureAwait(false);
            return response.HasValue ? Map(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<OAuthConsentRecord> UpsertAsync(
        OAuthConsentRecord consent,
        CancellationToken ct = default)
    {
        try
        {
            var entity = Map(consent);
            await IdentityTenantPurgePartition.Upsert(Table(), entity, TableUpdateMode.Replace, _options.RequireTenantPurgeFencing, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<bool> RevokeAsync(
        Guid userId,
        Guid tenantId,
        string clientId,
        string resource,
        DateTimeOffset revokedUtc,
        CancellationToken ct = default)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var response = await Table().GetEntityIfExistsAsync<OAuthConsentEntity>(
                    _options.OAuthConsentsPk(tenantId, userId),
                    _options.OAuthConsentsRk(clientId, resource),
                    cancellationToken: ct).ConfigureAwait(false);
                if (!response.HasValue)
                    return false;
                if (response.Value.RevokedUtc is not null)
                    return true;

                response.Value.RevokedUtc = revokedUtc;
                response.Value.UpdatedUtc = revokedUtc;
                try
                {
                    await IdentityTenantPurgePartition.Update(Table(),
                        response.Value,
                        response.Value.ETag,
                        TableUpdateMode.Replace, _options.RequireTenantPurgeFencing, ct).ConfigureAwait(false);
                    return true;
                }
                catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
                {
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private TableClient Table() =>
        _serviceClient.GetTableClient(_options.FullTableName(_options.OAuthConsentsTableName));

    private OAuthConsentEntity Map(OAuthConsentRecord consent) =>
        new()
        {
            PartitionKey = _options.OAuthConsentsPk(consent.TenantId, consent.UserId),
            RowKey = _options.OAuthConsentsRk(consent.ClientId, consent.Resource),
            ConsentId = consent.ConsentId.ToString("D"),
            UserId = consent.UserId.ToString("D"),
            TenantId = consent.TenantId.ToString("D"),
            ClientId = consent.ClientId,
            Resource = consent.Resource,
            ScopesJson = OAuthTableSerialization.Write(consent.Scopes),
            CreatedUtc = consent.CreatedUtc,
            UpdatedUtc = consent.UpdatedUtc,
            RevokedUtc = consent.RevokedUtc
        };

    private static OAuthConsentRecord Map(OAuthConsentEntity entity) =>
        new(
            Guid.Parse(entity.ConsentId),
            Guid.Parse(entity.UserId),
            Guid.Parse(entity.TenantId),
            entity.ClientId,
            entity.Resource,
            OAuthTableSerialization.Read(entity.ScopesJson),
            entity.CreatedUtc,
            entity.UpdatedUtc,
            entity.RevokedUtc);
}
