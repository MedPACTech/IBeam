using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableOAuthClientStore : IOAuthClientStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _options;

    public AzureTableOAuthClientStore(
        TableServiceClient serviceClient,
        IOptions<AzureTableIdentityOptions> options)
    {
        _serviceClient = serviceClient;
        _options = options.Value;
    }

    public async Task<OAuthClientRecord?> GetAsync(string clientId, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return null;

            var response = await Table().GetEntityIfExistsAsync<OAuthClientEntity>(
                AzureTableIdentityOptions.OAuthClientsPk,
                _options.OAuthClientsRk(clientId),
                cancellationToken: ct).ConfigureAwait(false);
            return response.HasValue ? Map(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<OAuthClientRecord>> ListByTenantAsync(
        Guid? tenantId,
        CancellationToken ct = default)
    {
        try
        {
            var tenantValue = tenantId?.ToString("D") ?? string.Empty;
            var clients = new List<OAuthClientRecord>();
            await foreach (var entity in Table().QueryAsync<OAuthClientEntity>(
                x => x.PartitionKey == AzureTableIdentityOptions.OAuthClientsPk && x.TenantId == tenantValue,
                cancellationToken: ct).ConfigureAwait(false))
            {
                clients.Add(Map(entity));
            }

            return clients
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ClientId, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<OAuthClientRecord> CreateAsync(OAuthClientRecord client, CancellationToken ct = default)
    {
        try
        {
            var entity = Map(client);
            await Table().AddEntityAsync(entity, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<OAuthClientRecord> UpdateAsync(OAuthClientRecord client, CancellationToken ct = default)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var current = await Table().GetEntityIfExistsAsync<OAuthClientEntity>(
                    AzureTableIdentityOptions.OAuthClientsPk,
                    _options.OAuthClientsRk(client.ClientId),
                    cancellationToken: ct).ConfigureAwait(false);
                if (!current.HasValue)
                    throw new IdentityNotFoundException($"OAuth client '{client.ClientId}' was not found.");

                var updated = Map(client);
                updated.ETag = current.Value.ETag;
                try
                {
                    await Table().UpdateEntityAsync(updated, updated.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                    return Map(updated);
                }
                catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
                {
                }
            }

            throw new IdentityProviderException("Failed to update OAuth client due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private TableClient Table() =>
        _serviceClient.GetTableClient(_options.FullTableName(_options.OAuthClientsTableName));

    private OAuthClientEntity Map(OAuthClientRecord client) =>
        new()
        {
            PartitionKey = AzureTableIdentityOptions.OAuthClientsPk,
            RowKey = _options.OAuthClientsRk(client.ClientId),
            ClientId = client.ClientId,
            TenantId = client.TenantId?.ToString("D") ?? string.Empty,
            DisplayName = client.DisplayName,
            ClientType = client.ClientType,
            RedirectUrisJson = OAuthTableSerialization.Write(client.RedirectUris),
            AllowedGrantTypesJson = OAuthTableSerialization.Write(client.AllowedGrantTypes),
            AllowedScopesJson = OAuthTableSerialization.Write(client.AllowedScopes),
            AllowedResourcesJson = OAuthTableSerialization.Write(client.AllowedResources),
            RequirePkce = client.RequirePkce,
            Status = client.Status,
            ClientSecretHash = client.ClientSecretHash,
            ClientSecretHashAlgorithm = client.ClientSecretHashAlgorithm,
            CreatedUtc = client.CreatedUtc,
            UpdatedUtc = client.UpdatedUtc,
            SecretRotatedUtc = client.SecretRotatedUtc,
            ClientSecretExpiresUtc = client.ClientSecretExpiresUtc,
            DisabledUtc = client.DisabledUtc,
            RevokedUtc = client.RevokedUtc
        };

    private static OAuthClientRecord Map(OAuthClientEntity entity) =>
        new(
            entity.ClientId,
            ParseNullableGuid(entity.TenantId),
            entity.DisplayName,
            entity.ClientType,
            OAuthTableSerialization.Read(entity.RedirectUrisJson),
            OAuthTableSerialization.Read(entity.AllowedGrantTypesJson),
            OAuthTableSerialization.Read(entity.AllowedScopesJson),
            OAuthTableSerialization.Read(entity.AllowedResourcesJson),
            entity.RequirePkce,
            entity.Status,
            entity.ClientSecretHash,
            entity.ClientSecretHashAlgorithm,
            entity.CreatedUtc,
            entity.UpdatedUtc,
            entity.SecretRotatedUtc,
            entity.ClientSecretExpiresUtc,
            entity.DisabledUtc,
            entity.RevokedUtc);

    private static Guid? ParseNullableGuid(string? value) =>
        Guid.TryParse(value, out var result) ? result : null;
}
