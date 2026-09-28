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

// Two independent secrets need their own lookup path here: the device_code (known only to the
// polling CLI) and the user_code (known only to the approving browser). Azure Table only supports
// efficient point lookups on PK+RK, so each record is stored as a primary row keyed by the
// device_code hash plus a small pointer row keyed by the user_code hash that just holds the
// device_code hash. See OAuthDeviceAuthorizationEntity.cs for the row shapes.
public sealed class AzureTableOAuthDeviceAuthorizationStore : IOAuthDeviceAuthorizationStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _options;

    public AzureTableOAuthDeviceAuthorizationStore(
        TableServiceClient serviceClient,
        IOptions<AzureTableIdentityOptions> options)
    {
        _serviceClient = serviceClient;
        _options = options.Value;
    }

    public async Task<OAuthDeviceAuthorizationRecord> CreateAsync(
        OAuthDeviceAuthorizationRecord authorization,
        CancellationToken ct = default)
    {
        try
        {
            var entity = Map(authorization);
            await Table().AddEntityAsync(entity, ct).ConfigureAwait(false);
            // Not atomic with the write above: if this second write fails, the primary row exists
            // but isn't approvable by user_code yet. Fails closed (the approval page just won't
            // find it) rather than leaving a half-written record that looks consumable - the CLI
            // can request a fresh code.
            await Table().AddEntityAsync(IndexEntity(authorization.UserCode, authorization.DeviceCodeHash), ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<OAuthDeviceAuthorizationRecord?> GetByDeviceCodeHashAsync(
        string deviceCodeHash,
        CancellationToken ct = default)
    {
        try
        {
            var response = await GetPrimaryEntityAsync(deviceCodeHash, ct).ConfigureAwait(false);
            return response.HasValue ? Map(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<OAuthDeviceAuthorizationRecord?> GetByUserCodeAsync(
        string userCode,
        CancellationToken ct = default)
    {
        try
        {
            var deviceCodeHash = await ResolveDeviceCodeHashAsync(userCode, ct).ConfigureAwait(false);
            if (deviceCodeHash is null) return null;
            var response = await GetPrimaryEntityAsync(deviceCodeHash, ct).ConfigureAwait(false);
            return response.HasValue ? Map(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<OAuthDeviceAuthorizationRecord?> TryApproveAsync(
        string userCode,
        Guid userId,
        Guid tenantId,
        IReadOnlyList<string> grantedScopes,
        DateTimeOffset approvedUtc,
        CancellationToken ct = default) =>
        await TryUpdateAsync(userCode, entity =>
        {
            entity.UserId = userId.ToString("D");
            entity.TenantId = tenantId.ToString("D");
            entity.GrantedScopesJson = OAuthTableSerialization.Write(grantedScopes);
            entity.ApprovedUtc = approvedUtc;
        }, ct).ConfigureAwait(false);

    public async Task<OAuthDeviceAuthorizationRecord?> TryDenyAsync(
        string userCode,
        DateTimeOffset deniedUtc,
        CancellationToken ct = default) =>
        await TryUpdateAsync(userCode, entity => entity.DeniedUtc = deniedUtc, ct).ConfigureAwait(false);

    public async Task<OAuthDeviceAuthorizationRecord?> TryConsumeAsync(
        string deviceCodeHash,
        DateTimeOffset consumedUtc,
        CancellationToken ct = default)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var current = await GetPrimaryEntityAsync(deviceCodeHash, ct).ConfigureAwait(false);
                if (!current.HasValue) return null;

                // A denied record is deliberately still consumable - DeviceCodeAsync calls this
                // on every poll after a denial so it keeps returning access_denied, not
                // invalid_grant, without a second store method just for that one distinction.
                var mapped = Map(current.Value);
                if (!mapped.IsDenied && !mapped.IsUsable(consumedUtc)) return null;

                current.Value.ConsumedUtc = consumedUtc;
                try
                {
                    await Table().UpdateEntityAsync(current.Value, current.Value.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                    return Map(current.Value);
                }
                catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
                {
                }
            }

            throw new IdentityProviderException("Failed to consume OAuth device authorization due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<bool> RecordPollAsync(
        string deviceCodeHash,
        DateTimeOffset polledUtc,
        int minIntervalSeconds,
        CancellationToken ct = default)
    {
        try
        {
            var current = await GetPrimaryEntityAsync(deviceCodeHash, ct).ConfigureAwait(false);
            if (!current.HasValue) return false;

            var tooSoon = current.Value.LastPolledUtc is { } last && (polledUtc - last).TotalSeconds < minIntervalSeconds;
            current.Value.LastPolledUtc = polledUtc;
            try
            {
                await Table().UpdateEntityAsync(current.Value, current.Value.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // Lost the race to another concurrent poll recording its own timestamp - fine to
                // drop, see the interface doc: this is a politeness check, not a security one.
            }
            return tooSoon;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private async Task<OAuthDeviceAuthorizationRecord?> TryUpdateAsync(
        string userCode,
        Action<OAuthDeviceAuthorizationEntity> apply,
        CancellationToken ct)
    {
        try
        {
            var deviceCodeHash = await ResolveDeviceCodeHashAsync(userCode, ct).ConfigureAwait(false);
            if (deviceCodeHash is null) return null;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var current = await GetPrimaryEntityAsync(deviceCodeHash, ct).ConfigureAwait(false);
                if (!current.HasValue) return null;
                if (!Map(current.Value).IsPending) return null;

                apply(current.Value);
                try
                {
                    await Table().UpdateEntityAsync(current.Value, current.Value.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                    return Map(current.Value);
                }
                catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
                {
                }
            }

            throw new IdentityProviderException("Failed to update OAuth device authorization due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private async Task<string?> ResolveDeviceCodeHashAsync(string userCode, CancellationToken ct)
    {
        var response = await Table().GetEntityIfExistsAsync<OAuthDeviceUserCodeIndexEntity>(
            _options.OAuthDeviceUserCodeIndexPk(userCode),
            _options.OAuthDeviceUserCodeIndexRk(userCode),
            cancellationToken: ct).ConfigureAwait(false);
        return response.HasValue ? response.Value!.DeviceCodeHash : null;
    }

    private async Task<NullableResponse<OAuthDeviceAuthorizationEntity>> GetPrimaryEntityAsync(
        string deviceCodeHash,
        CancellationToken ct) =>
        await Table().GetEntityIfExistsAsync<OAuthDeviceAuthorizationEntity>(
            _options.OAuthDeviceAuthorizationsPk(deviceCodeHash),
            _options.OAuthDeviceAuthorizationsRk(deviceCodeHash),
            cancellationToken: ct).ConfigureAwait(false);

    private TableClient Table() =>
        _serviceClient.GetTableClient(_options.FullTableName(_options.OAuthDeviceAuthorizationsTableName));

    private OAuthDeviceUserCodeIndexEntity IndexEntity(string userCode, string deviceCodeHash) =>
        new()
        {
            PartitionKey = _options.OAuthDeviceUserCodeIndexPk(userCode),
            RowKey = _options.OAuthDeviceUserCodeIndexRk(userCode),
            DeviceCodeHash = deviceCodeHash
        };

    private OAuthDeviceAuthorizationEntity Map(OAuthDeviceAuthorizationRecord record) =>
        new()
        {
            PartitionKey = _options.OAuthDeviceAuthorizationsPk(record.DeviceCodeHash),
            RowKey = _options.OAuthDeviceAuthorizationsRk(record.DeviceCodeHash),
            DeviceCodeHash = record.DeviceCodeHash,
            UserCode = record.UserCode,
            ClientId = record.ClientId,
            ScopesJson = OAuthTableSerialization.Write(record.Scopes),
            Resource = record.Resource,
            CreatedUtc = record.CreatedUtc,
            ExpiresUtc = record.ExpiresUtc,
            IntervalSeconds = record.IntervalSeconds,
            UserId = record.UserId?.ToString("D"),
            TenantId = record.TenantId?.ToString("D"),
            GrantedScopesJson = record.GrantedScopes is null ? null : OAuthTableSerialization.Write(record.GrantedScopes),
            ApprovedUtc = record.ApprovedUtc,
            DeniedUtc = record.DeniedUtc,
            ConsumedUtc = record.ConsumedUtc,
            LastPolledUtc = record.LastPolledUtc
        };

    private static OAuthDeviceAuthorizationRecord Map(OAuthDeviceAuthorizationEntity entity) =>
        new(
            entity.DeviceCodeHash,
            entity.UserCode,
            entity.ClientId,
            OAuthTableSerialization.Read(entity.ScopesJson),
            entity.Resource,
            entity.CreatedUtc,
            entity.ExpiresUtc,
            entity.IntervalSeconds,
            string.IsNullOrEmpty(entity.UserId) ? null : Guid.Parse(entity.UserId),
            string.IsNullOrEmpty(entity.TenantId) ? null : Guid.Parse(entity.TenantId),
            entity.GrantedScopesJson is null ? null : OAuthTableSerialization.Read(entity.GrantedScopesJson),
            entity.ApprovedUtc,
            entity.DeniedUtc,
            entity.ConsumedUtc,
            entity.LastPolledUtc);
}
