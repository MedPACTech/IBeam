using Azure.Data.Tables;
using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableExternalLoginStore : IExternalLoginStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableExternalLoginStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<ExternalLoginInfo?> FindByProviderAsync(string provider, string providerUserId, CancellationToken ct = default)
    {
        try
        {
            var normalizedProvider = Normalize(provider);
            var normalizedProviderUserId = Normalize(providerUserId);

            var table = GetTable();
            var response = await table.GetEntityIfExistsAsync<ExternalLoginEntity>(
                PartitionForProvider(normalizedProvider),
                RowForProviderUserId(normalizedProviderUserId),
                cancellationToken: ct).ConfigureAwait(false);

            return response.HasValue ? ToModel(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<ExternalLoginInfo?> FindByUserAndProviderAsync(Guid userId, string provider, CancellationToken ct = default)
    {
        try
        {
            var normalizedProvider = Normalize(provider);
            var userIdString = userId.ToString("D");
            var table = GetTable();

            await foreach (var e in table.QueryAsync<ExternalLoginEntity>(
                x => x.PartitionKey == PartitionForProvider(normalizedProvider) && x.UserId == userIdString,
                cancellationToken: ct).ConfigureAwait(false))
            {
                return ToModel(e);
            }

            return null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<ExternalLoginInfo>> GetByUserAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var userIdString = userId.ToString("D");
            var table = GetTable();
            var results = new List<ExternalLoginInfo>();

            await foreach (var e in table.QueryAsync<ExternalLoginEntity>(x => x.UserId == userIdString, cancellationToken: ct).ConfigureAwait(false))
                results.Add(ToModel(e));

            return results;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task LinkAsync(Guid userId, string provider, string providerUserId, string? email = null, CancellationToken ct = default)
    {
        try
        {
            var normalizedProvider = Normalize(provider);
            var normalizedProviderUserId = Normalize(providerUserId);
            var existing = await FindByProviderAsync(normalizedProvider, normalizedProviderUserId, ct);
            if (existing is not null && existing.UserId != userId)
                throw new IdentityValidationException("This external login is already linked to another user.");

            var table = GetTable();
            await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);

            var entity = new ExternalLoginEntity
            {
                PartitionKey = PartitionForProvider(normalizedProvider),
                RowKey = RowForProviderUserId(normalizedProviderUserId),
                UserId = userId.ToString("D"),
                Provider = normalizedProvider,
                ProviderUserId = normalizedProviderUserId,
                Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
                LinkedAt = DateTimeOffset.UtcNow
            };

            await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<bool> UnlinkAsync(Guid userId, string provider, CancellationToken ct = default)
    {
        try
        {
            var normalizedProvider = Normalize(provider);
            var table = GetTable();
            var userIdString = userId.ToString("D");

            await foreach (var e in table.QueryAsync<ExternalLoginEntity>(
                x => x.PartitionKey == PartitionForProvider(normalizedProvider) && x.UserId == userIdString,
                cancellationToken: ct).ConfigureAwait(false))
            {
                await table.DeleteEntityAsync(e.PartitionKey, e.RowKey, e.ETag, ct).ConfigureAwait(false);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private TableClient GetTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.ExternalLoginsTableName));

    private static string PartitionForProvider(string provider) => $"PROV|{provider}";
    private static string RowForProviderUserId(string providerUserId) => $"PID|{providerUserId}";
    private static string Normalize(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static ExternalLoginInfo ToModel(ExternalLoginEntity e)
        => new(
            UserId: Guid.Parse(e.UserId),
            Provider: e.Provider,
            ProviderUserId: e.ProviderUserId,
            Email: e.Email);
}
