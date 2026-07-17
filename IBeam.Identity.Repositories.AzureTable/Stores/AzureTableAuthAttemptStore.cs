using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableAuthAttemptStore : IAuthAttemptStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableAuthAttemptStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<AuthAttemptState> GetStateAsync(string method, string identifier, CancellationToken ct = default)
    {
        var table = Table();
        var response = await table.GetEntityIfExistsAsync<AuthAttemptEntity>(Partition(method), Row(identifier), cancellationToken: ct).ConfigureAwait(false);
        if (!response.HasValue)
            return new AuthAttemptState(0, null, null, null);

        return ToState(response.Value);
    }

    public async Task<AuthAttemptState> RegisterFailureAsync(
        string method,
        string identifier,
        int maxFailedAttempts,
        TimeSpan lockoutDuration,
        CancellationToken ct = default,
        AuthAttemptContext? context = null)
    {
        var table = Table();
        if (_opts.CreateTablesIfNotExists)
        {
            await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;

        var response = await table.GetEntityIfExistsAsync<AuthAttemptEntity>(Partition(method), Row(identifier), cancellationToken: ct).ConfigureAwait(false);
        var entity = response.HasValue
            ? response.Value
            : new AuthAttemptEntity
            {
                PartitionKey = Partition(method),
                RowKey = Row(identifier),
                Method = Normalize(method),
                Identifier = Normalize(identifier)
            };

        entity.FailedAttempts += 1;
        entity.LastFailedAtUtc = now;
        entity.LastSucceededAtUtc = null;
        entity.LockedUntilUtc = entity.FailedAttempts >= maxFailedAttempts ? now.Add(lockoutDuration) : null;
        ApplyFailureContext(entity, context);

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        return ToState(entity);
    }

    public async Task<AuthAttemptState> RegisterSuccessAsync(
        string method,
        string identifier,
        CancellationToken ct = default,
        AuthAttemptContext? context = null)
    {
        var table = Table();
        if (_opts.CreateTablesIfNotExists)
        {
            await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new AuthAttemptEntity
        {
            PartitionKey = Partition(method),
            RowKey = Row(identifier),
            Method = Normalize(method),
            Identifier = Normalize(identifier),
            FailedAttempts = 0,
            LockedUntilUtc = null,
            LastFailedAtUtc = null,
            LastSucceededAtUtc = now
        };
        ApplySuccessContext(entity, context);

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        return ToState(entity);
    }

    public async Task<AuthAttemptState> UnlockAsync(
        string method,
        string identifier,
        Guid? unlockedByUserId = null,
        string? reason = null,
        CancellationToken ct = default,
        AuthAttemptContext? context = null)
    {
        var table = Table();
        if (_opts.CreateTablesIfNotExists)
        {
            await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;

        var response = await table.GetEntityIfExistsAsync<AuthAttemptEntity>(Partition(method), Row(identifier), cancellationToken: ct)
            .ConfigureAwait(false);
        var entity = response.HasValue
            ? response.Value
            : new AuthAttemptEntity
            {
                PartitionKey = Partition(method),
                RowKey = Row(identifier),
                Method = Normalize(method),
                Identifier = Normalize(identifier)
            };

        entity.FailedAttempts = 0;
        entity.LockedUntilUtc = null;
        entity.LastUnlockedAtUtc = now;
        entity.UnlockedByUserId = unlockedByUserId?.ToString("D");
        entity.UnlockReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ApplyGeneralContext(entity, context);

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        return ToState(entity);
    }

    public async Task ClearAsync(string method, string identifier, CancellationToken ct = default)
    {
        try
        {
            await Table().DeleteEntityAsync(Partition(method), Row(identifier), cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }

    private TableClient Table()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.AuthAttemptsTableName));

    private static string Partition(string method) => $"MTH|{Normalize(method)}";
    private static string Row(string identifier) => $"IDN|{Normalize(identifier)}";

    private static string Normalize(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static AuthAttemptState ToState(AuthAttemptEntity entity)
        => new(
            entity.FailedAttempts,
            entity.LockedUntilUtc,
            entity.LastFailedAtUtc,
            entity.LastSucceededAtUtc,
            entity.LastFailedIp,
            entity.LastSucceededIp,
            entity.LastUserAgent,
            entity.LastDeviceId,
            entity.LastCountry,
            entity.LastRegion,
            entity.LastCity,
            entity.LastCorrelationId,
            entity.LastUnlockedAtUtc,
            entity.UnlockedByUserId,
            entity.UnlockReason,
            entity.MetadataJson);

    private static void ApplyFailureContext(AuthAttemptEntity entity, AuthAttemptContext? context)
    {
        if (context is null)
            return;

        entity.LastFailedIp = NormalizeOptional(context.IpAddress);
        ApplyGeneralContext(entity, context);
    }

    private static void ApplySuccessContext(AuthAttemptEntity entity, AuthAttemptContext? context)
    {
        if (context is null)
            return;

        entity.LastSucceededIp = NormalizeOptional(context.IpAddress);
        ApplyGeneralContext(entity, context);
    }

    private static void ApplyGeneralContext(AuthAttemptEntity entity, AuthAttemptContext? context)
    {
        if (context is null)
            return;

        entity.LastUserAgent = NormalizeOptional(context.UserAgent);
        entity.LastDeviceId = NormalizeOptional(context.DeviceId);
        entity.LastCountry = NormalizeOptional(context.Country);
        entity.LastRegion = NormalizeOptional(context.Region);
        entity.LastCity = NormalizeOptional(context.City);
        entity.LastCorrelationId = NormalizeOptional(context.CorrelationId);
        entity.MetadataJson = context.Metadata is { Count: > 0 }
            ? JsonSerializer.Serialize(context.Metadata)
            : entity.MetadataJson;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
