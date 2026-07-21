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

public sealed class AzureTableTenantInviteStore : ITenantInviteStore
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
            await Table().AddEntityAsync(ToEntity(invite), ct).ConfigureAwait(false);
            var tokenEntity = ToEntity(invite);
            tokenEntity.PartitionKey = TokenPk(invite.TokenHash);
            tokenEntity.RowKey = TokenRk();
            await Table().AddEntityAsync(tokenEntity, ct).ConfigureAwait(false);
            return invite;
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

            return response.HasValue ? ToRecord(response.Value) : null;
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
            var existing = await GetAsync(invite.TenantId, invite.InviteId, ct).ConfigureAwait(false);
            if (existing is not null &&
                !string.Equals(existing.TokenHash, invite.TokenHash, StringComparison.Ordinal))
            {
                try
                {
                    await Table().DeleteEntityAsync(TokenPk(existing.TokenHash), TokenRk(), cancellationToken: ct)
                        .ConfigureAwait(false);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                }
            }

            var tenantEntity = ToEntity(invite);
            await Table().UpsertEntityAsync(tenantEntity, TableUpdateMode.Replace, ct).ConfigureAwait(false);

            var tokenEntity = ToEntity(invite);
            tokenEntity.PartitionKey = TokenPk(invite.TokenHash);
            tokenEntity.RowKey = TokenRk();
            await Table().UpsertEntityAsync(tokenEntity, TableUpdateMode.Replace, ct).ConfigureAwait(false);

            return invite;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private TenantInviteEntity ToEntity(TenantInviteRecord invite)
        => new()
        {
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
            DisplayName = invite.ProfileHints?.DisplayName,
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
                entity.DisplayName,
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
            entity.RequirePasswordSetup);
    }

    private static Guid? ParseNullableGuid(string? value)
        => Guid.TryParse(value, out var guid) ? guid : null;

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
