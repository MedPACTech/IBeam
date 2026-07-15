using IBeam.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace IBeam.Services.Logging;

public sealed class LoggerAuditTrailSink : IAuditTrailSink
{
    private readonly ILogger<LoggerAuditTrailSink> _logger;

    public LoggerAuditTrailSink(ILogger<LoggerAuditTrailSink> logger)
    {
        _logger = logger;
    }

    public Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "IBeamAudit Transaction Service={Service} Entity={Entity} Operation={Operation} Action={Action} EntityId={EntityId} TenantId={TenantId} Actor={Actor} IpAddress={IpAddress}",
            transaction.ServiceName,
            transaction.EntityName,
            transaction.Operation,
            string.IsNullOrWhiteSpace(transaction.Action) ? transaction.Operation.ToString() : transaction.Action,
            transaction.EntityId,
            transaction.TenantId,
            transaction.ActorId ?? "<none>",
            transaction.IpAddress ?? "<none>");

        return Task.CompletedTask;
    }

    public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "IBeamAudit SelectRollup Date={DateUtc} Service={Service} Entity={Entity} Operation={Operation} Action={Action} Signature={Signature} Count={Count}",
            rollup.DateUtc,
            rollup.ServiceName,
            rollup.EntityName,
            rollup.Operation,
            string.IsNullOrWhiteSpace(rollup.Action) ? rollup.Operation.ToString() : rollup.Action,
            rollup.QuerySignature,
            rollup.Count);

        return Task.CompletedTask;
    }
}
