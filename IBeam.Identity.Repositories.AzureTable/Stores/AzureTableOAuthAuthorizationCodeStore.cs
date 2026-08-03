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

public sealed class AzureTableOAuthAuthorizationCodeStore : IOAuthAuthorizationCodeStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _options;

    public AzureTableOAuthAuthorizationCodeStore(
        TableServiceClient serviceClient,
        IOptions<AzureTableIdentityOptions> options)
    {
        _serviceClient = serviceClient;
        _options = options.Value;
    }

    public async Task<OAuthAuthorizationCodeRecord> CreateAsync(
        OAuthAuthorizationCodeRecord authorizationCode,
        CancellationToken ct = default)
    {
        try
        {
            var entity = Map(authorizationCode);
            await Table().AddEntityAsync(entity, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<OAuthAuthorizationCodeRecord?> GetByHashAsync(
        string codeHash,
        CancellationToken ct = default)
    {
        try
        {
            var response = await GetEntityAsync(codeHash, ct).ConfigureAwait(false);
            return response.HasValue ? Map(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<OAuthAuthorizationCodeRecord?> TryConsumeAsync(
        string codeHash,
        DateTimeOffset consumedUtc,
        CancellationToken ct = default)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var current = await GetEntityAsync(codeHash, ct).ConfigureAwait(false);
                if (!current.HasValue)
                    return null;

                if (!Map(current.Value).IsUsable(consumedUtc))
                    return null;

                current.Value.ConsumedUtc = consumedUtc;
                try
                {
                    await Table().UpdateEntityAsync(
                        current.Value,
                        current.Value.ETag,
                        TableUpdateMode.Replace,
                        ct).ConfigureAwait(false);
                    return Map(current.Value);
                }
                catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
                {
                }
            }

            throw new IdentityProviderException("Failed to consume OAuth authorization code due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private async Task<NullableResponse<OAuthAuthorizationCodeEntity>> GetEntityAsync(
        string codeHash,
        CancellationToken ct) =>
        await Table().GetEntityIfExistsAsync<OAuthAuthorizationCodeEntity>(
            _options.OAuthAuthorizationCodesPk(codeHash),
            _options.OAuthAuthorizationCodesRk(codeHash),
            cancellationToken: ct).ConfigureAwait(false);

    private TableClient Table() =>
        _serviceClient.GetTableClient(_options.FullTableName(_options.OAuthAuthorizationCodesTableName));

    private OAuthAuthorizationCodeEntity Map(OAuthAuthorizationCodeRecord code) =>
        new()
        {
            PartitionKey = _options.OAuthAuthorizationCodesPk(code.CodeHash),
            RowKey = _options.OAuthAuthorizationCodesRk(code.CodeHash),
            CodeHash = code.CodeHash,
            ClientId = code.ClientId,
            RedirectUri = code.RedirectUri,
            UserId = code.UserId.ToString("D"),
            TenantId = code.TenantId.ToString("D"),
            ScopesJson = OAuthTableSerialization.Write(code.Scopes),
            Resource = code.Resource,
            CodeChallenge = code.CodeChallenge,
            CodeChallengeMethod = code.CodeChallengeMethod,
            CreatedUtc = code.CreatedUtc,
            ExpiresUtc = code.ExpiresUtc,
            ConsumedUtc = code.ConsumedUtc
        };

    private static OAuthAuthorizationCodeRecord Map(OAuthAuthorizationCodeEntity entity) =>
        new(
            entity.CodeHash,
            entity.ClientId,
            entity.RedirectUri,
            Guid.Parse(entity.UserId),
            Guid.Parse(entity.TenantId),
            OAuthTableSerialization.Read(entity.ScopesJson),
            entity.Resource,
            entity.CodeChallenge,
            entity.CodeChallengeMethod,
            entity.CreatedUtc,
            entity.ExpiresUtc,
            entity.ConsumedUtc);
}
