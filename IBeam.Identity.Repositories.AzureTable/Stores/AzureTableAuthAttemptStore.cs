using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;

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

    public async Task<AuthAttemptState> RegisterFailureAsync(string method, string identifier, int maxFailedAttempts, TimeSpan lockoutDuration, CancellationToken ct = default)
    {
        var table = Table();
        await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
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

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        return ToState(entity);
    }

    public async Task<AuthAttemptState> RegisterSuccessAsync(string method, string identifier, CancellationToken ct = default)
    {
        var table = Table();
        await table.CreateIfNotExistsAsync(ct).ConfigureAwait(false);

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

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        return ToState(entity);
    }

    private TableClient Table()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.AuthAttemptsTableName));

    private static string Partition(string method) => $"MTH|{Normalize(method)}";
    private static string Row(string identifier) => $"IDN|{Normalize(identifier)}";

    private static string Normalize(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static AuthAttemptState ToState(AuthAttemptEntity entity)
        => new(entity.FailedAttempts, entity.LockedUntilUtc, entity.LastFailedAtUtc, entity.LastSucceededAtUtc);
}
