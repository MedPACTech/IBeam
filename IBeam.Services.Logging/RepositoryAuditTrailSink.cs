using IBeam.Repositories.Abstractions;
using IBeam.Services.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace IBeam.Services.Logging;

public sealed class RepositoryAuditTrailSink : IAuditTrailSink
{
    private readonly IBaseRepositoryAsync<ServiceAuditLogEntry> _repository;

    public RepositoryAuditTrailSink(IBaseRepositoryAsync<ServiceAuditLogEntry> repository)
    {
        _repository = repository;
    }

    public async Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default)
    {
        var entry = new ServiceAuditLogEntry
        {
            Id = Guid.NewGuid(),
            IsDeleted = false,
            OccurredUtc = transaction.OccurredUtc,
            DateUtc = DateOnly.FromDateTime(transaction.OccurredUtc.UtcDateTime),
            ServiceName = transaction.ServiceName,
            EntityName = transaction.EntityName,
            Operation = transaction.Operation.ToString(),
            EntityId = transaction.EntityId,
            TenantId = transaction.TenantId,
            ActorId = transaction.ActorId,
            CorrelationId = transaction.CorrelationId,
            OriginalJson = transaction.OriginalJson,
            TransformedJson = transaction.TransformedJson,
            IsSelectRollup = false,
            QuerySignature = null,
            FirstSeenUtc = transaction.OccurredUtc,
            LastSeenUtc = transaction.OccurredUtc,
            Count = 1
        };

        await _repository.SaveAsync(entry, ct).ConfigureAwait(false);
    }

    public async Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default)
    {
        var id = BuildDailyRollupId(rollup);
        var now = DateTimeOffset.UtcNow;

        var existing = await _repository.GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
        if (existing is null)
        {
            var entry = new ServiceAuditLogEntry
            {
                Id = id,
                IsDeleted = false,
                OccurredUtc = now,
                DateUtc = rollup.DateUtc,
                ServiceName = rollup.ServiceName,
                EntityName = rollup.EntityName,
                Operation = rollup.Operation.ToString(),
                EntityId = null,
                TenantId = rollup.TenantId,
                ActorId = rollup.ActorId,
                CorrelationId = null,
                OriginalJson = null,
                TransformedJson = null,
                IsSelectRollup = true,
                QuerySignature = rollup.QuerySignature,
                FirstSeenUtc = rollup.FirstSeenUtc,
                LastSeenUtc = rollup.LastSeenUtc,
                Count = Math.Max(rollup.Count, 1)
            };

            await _repository.SaveAsync(entry, ct).ConfigureAwait(false);
            return;
        }

        existing.LastSeenUtc = rollup.LastSeenUtc;
        existing.Count += Math.Max(rollup.Count, 1);
        await _repository.SaveAsync(existing, ct).ConfigureAwait(false);
    }

    public static Guid BuildDailyRollupId(ServiceSelectAuditRollup rollup)
    {
        var key = string.Join("|",
            rollup.DateUtc,
            rollup.ServiceName,
            rollup.EntityName,
            rollup.Operation,
            rollup.TenantId,
            rollup.ActorId,
            rollup.QuerySignature);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }
}
