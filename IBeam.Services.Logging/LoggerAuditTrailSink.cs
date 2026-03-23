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
            "IBeamAudit Transaction Service={Service} Entity={Entity} Operation={Operation} EntityId={EntityId} TenantId={TenantId} Actor={Actor}",
            transaction.ServiceName,
            transaction.EntityName,
            transaction.Operation,
            transaction.EntityId,
            transaction.TenantId,
            transaction.ActorId ?? "<none>");

        return Task.CompletedTask;
    }

    public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "IBeamAudit SelectRollup Date={DateUtc} Service={Service} Entity={Entity} Operation={Operation} Signature={Signature} Count={Count}",
            rollup.DateUtc,
            rollup.ServiceName,
            rollup.EntityName,
            rollup.Operation,
            rollup.QuerySignature,
            rollup.Count);

        return Task.CompletedTask;
    }
}
