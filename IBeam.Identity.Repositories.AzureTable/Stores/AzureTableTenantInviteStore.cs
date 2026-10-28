using System.Text.Json;
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

public sealed partial class AzureTableTenantInviteStore : ITenantInviteStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableTenantInviteStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<TenantInviteRecord> CreateAsync(TenantInviteRecord invite, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await IdentityTenantPurgePartition.Add(Table(), ToEntity(invite), _opts.RequireTenantPurgeFencing, ct).ConfigureAwait(false);
            var tokenEntity = ToEntity(invite);
            tokenEntity.PartitionKey = TokenPk(invite.TokenHash);
            tokenEntity.RowKey = TokenRk();
            await IdentityTenantPurgePartition.Add(Table(), tokenEntity, _opts.RequireTenantPurgeFencing, ct).ConfigureAwait(false);
            return await GetAsync(invite.TenantId, invite.InviteId, ct).ConfigureAwait(false) ?? throw new IdentityConcurrencyException();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<TenantInviteRecord>> ListForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var list = new List<TenantInviteRecord>();
            await foreach (var entity in Table().QueryAsync<TenantInviteEntity>(
                               x => x.PartitionKey == TenantPk(tenantId),
                               cancellationToken: ct).ConfigureAwait(false))
            {
                if (entity.RowKey.StartsWith(IdentityTenantPurgePartition.Prefix, StringComparison.Ordinal)) continue;
                list.Add(ToRecord(entity));
            }

            return list.OrderByDescending(x => x.CreatedUtc).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<TenantInviteRecord?> GetAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (await IdentityTenantPurgePartition.HasTenantFence(_serviceClient, _opts, tenantId, ct)) return null;
            var response = await Table().GetEntityIfExistsAsync<TenantInviteEntity>(
                    TenantPk(tenantId),
                    InviteRk(inviteId),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return response.HasValue ? ToRecord(response.Value) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<TenantInviteRecord?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var response = await Table().GetEntityIfExistsAsync<TenantInviteEntity>(
                    TokenPk(tokenHash),
                    TokenRk(),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            if (!response.HasValue) return null;
            // Token indexes may lag a cross-partition update or repair. The primary is authoritative.
            var index = ToRecord(response.Value);
            var current = await GetAsync(index.TenantId, index.InviteId, ct).ConfigureAwait(false);
            return current?.TokenHash == tokenHash ? current : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<TenantInviteRecord> UpdateAsync(TenantInviteRecord invite, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var existing = await GetAsync(invite.TenantId, invite.InviteId, ct).ConfigureAwait(false) ?? throw new IdentityConcurrencyException();
            if (existing.Status == TenantInviteStatuses.Redeemed && invite.Status == TenantInviteStatuses.Redeemed &&
                existing.RedeemedByUserId == invite.RedeemedByUserId && existing.AcceptanceId == invite.AcceptanceId) return existing;
            if (invite.Version is not null && invite.Version != existing.Version) throw new IdentityConcurrencyException();
            if (invite.Version is null && invite.TokenHash != existing.TokenHash) throw new IdentityConcurrencyException();
            if (existing.AcceptanceUserId is Guid accepting && (invite.Status != TenantInviteStatuses.Redeemed || invite.RedeemedByUserId != accepting ||
                invite.AcceptanceId != existing.AcceptanceId || invite.TokenHash != existing.TokenHash)) throw new IdentityConcurrencyException();
            if (existing.Status is TenantInviteStatuses.Redeemed or TenantInviteStatuses.Revoked && invite.Status != existing.Status) throw new IdentityConcurrencyException();
            try { await IdentityTenantPurgePartition.Update(Table(), ToEntity(invite), new ETag(existing.Version!), TableUpdateMode.Merge, _opts.RequireTenantPurgeFencing, ct).ConfigureAwait(false); }
            catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new IdentityConcurrencyException(); }
            var current = await GetAsync(invite.TenantId, invite.InviteId, ct).ConfigureAwait(false) ?? throw new IdentityConcurrencyException();
            await EnsureTokenIndex(current, ct);
            return current;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private TenantInviteEntity ToEntity(TenantInviteRecord invite)
        => new()
        {
            IBeamInviteAcceptanceId = invite.AcceptanceId?.ToString("D"),
            IBeamInviteAcceptanceUserId = invite.AcceptanceUserId?.ToString("D"),
            IBeamInviteLastOperationId = invite.LastTransitionOperationId?.ToString("D"),
            IBeamInviteLastResendUtc = invite.LastResendUtc,
            IBeamInviteResendWindowUtc = invite.ResendWindowUtc,
            IBeamInviteResendCount = invite.ResendCount,
            IBeamInviteRequireVerifiedAcceptance = invite.RequireVerifiedAcceptance,
            PartitionKey = TenantPk(invite.TenantId),
            RowKey = InviteRk(invite.InviteId),
            InviteId = invite.InviteId.ToString("D"),
            TenantId = invite.TenantId.ToString("D"),
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            TokenHash = invite.TokenHash,
            Status = invite.Status,
            CreatedUtc = invite.CreatedUtc,
            InvitedByUserId = invite.InvitedByUserId.ToString("D"),
            ExpiresUtc = invite.ExpiresUtc,
            SentUtc = invite.SentUtc,
            RedeemedUtc = invite.RedeemedUtc,
            RedeemedByUserId = invite.RedeemedByUserId?.ToString("D"),
            RevokedUtc = invite.RevokedUtc,
            RevokedByUserId = invite.RevokedByUserId?.ToString("D"),
            RevokedReason = invite.RevokedReason,
            FirstName = invite.ProfileHints?.FirstName,
            LastName = invite.ProfileHints?.LastName,
            ProfileMetadataJson = Serialize(invite.ProfileHints?.Metadata),
            RoleIdsCsv = string.Join(",", invite.RoleIds ?? []),
            RoleNamesJson = Serialize(invite.RoleNames),
            SetAsDefaultTenant = invite.SetAsDefaultTenant,
            AccessGrantsJson = Serialize(invite.AccessGrants),
            RedirectUrl = invite.RedirectUrl,
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            MetadataJson = Serialize(invite.Metadata),
            RequirePasswordSetup = invite.RequirePasswordSetup
        };

    private static TenantInviteRecord ToRecord(TenantInviteEntity entity)
    {
        var tenantId = Guid.Parse(entity.TenantId);
        var inviteId = Guid.Parse(entity.InviteId);
        if ((entity.IBeamInviteAcceptanceId is null) != (entity.IBeamInviteAcceptanceUserId is null))
            throw new IdentityProviderException("Invitation transition metadata is invalid.");
        return new TenantInviteRecord(
            inviteId,
            tenantId,
            entity.DestinationType,
            entity.NormalizedDestination,
            entity.TokenHash,
            entity.Status,
            entity.CreatedUtc,
            Guid.Parse(entity.InvitedByUserId),
            entity.ExpiresUtc,
            entity.SentUtc,
            entity.RedeemedUtc,
            ParseNullableGuid(entity.RedeemedByUserId),
            entity.RevokedUtc,
            ParseNullableGuid(entity.RevokedByUserId),
            entity.RevokedReason,
            new TenantInviteProfileHints(
                entity.FirstName,
                entity.LastName,
                Deserialize<Dictionary<string, string>>(entity.ProfileMetadataJson)),
            ParseGuidCsv(entity.RoleIdsCsv),
            Deserialize<List<string>>(entity.RoleNamesJson) ?? [],
            entity.SetAsDefaultTenant,
            Deserialize<List<TenantInviteAccessGrantRequest>>(entity.AccessGrantsJson) ?? [],
            entity.RedirectUrl,
            entity.CorrelationId,
            entity.CausationId,
            Deserialize<Dictionary<string, string>>(entity.MetadataJson),
            entity.RequirePasswordSetup) {
                Version = entity.ETag.ToString(), AcceptanceId = PrivateGuid(entity.IBeamInviteAcceptanceId),
                AcceptanceUserId = PrivateGuid(entity.IBeamInviteAcceptanceUserId), LastTransitionOperationId = PrivateGuid(entity.IBeamInviteLastOperationId),
                LastResendUtc = entity.IBeamInviteLastResendUtc, ResendWindowUtc = entity.IBeamInviteResendWindowUtc, ResendCount = entity.IBeamInviteResendCount,
                RequireVerifiedAcceptance = entity.IBeamInviteRequireVerifiedAcceptance };
    }

    private static Guid? ParseNullableGuid(string? value)
        => Guid.TryParse(value, out var guid) ? guid : null;
    private static Guid? PrivateGuid(string? value) => value is null ? null : Guid.TryParseExact(value, "D", out var guid) && guid != Guid.Empty
        ? guid : throw new IdentityProviderException("Invitation transition metadata is invalid.");

    private static List<Guid> ParseGuidCsv(string? csv)
        => (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();

    private static string? Serialize<T>(T? value)
        => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static T? Deserialize<T>(string? json)
        => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);

    private TableClient Table()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.TenantInvitesTableName));

    private static string TenantPk(Guid tenantId)
        => $"TEN|{tenantId:D}";

    private static string InviteRk(Guid inviteId)
        => $"INV|{inviteId:D}";

    private static string TokenPk(string tokenHash)
        => $"TOK|{tokenHash}";

    private static string TokenRk()
        => "INV";
}
