using IBeam.Management.Models;
using IBeam.Management.Services.Credentials;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace IBeam.Management.Services.Diagnostics;

public sealed class SystemLogReaderService(
    IManagementSystemLogReader reader,
    IManagementDiagnosticsAccessService access,
    SystemLogProjection projection,
    IServiceOperationExecutor operations,
    ILogger<SystemLogReaderService> logger) : ISystemLogReaderService
{
    [IBeamOperation(ManagementOperations.LogsRead, Permission = false)]
    public Task<SystemLogPage> ListAsync(SystemLogQuery query, CancellationToken ct = default)
        => operations.ExecuteAsync(this, token => SafelyAsync(async () =>
        {
            await access.RequireAsync(ManagementOperations.LogsRead, token);
            var page = await reader.ReadAsync(query, token);
            return new SystemLogPage(Array.AsReadOnly(page.Items.Select(x => projection.Project(x.Id, x.Record)).ToArray()),
                page.Query.PageSize, page.NextCursor);
        }), Audit(), ct);

    [IBeamOperation(ManagementOperations.LogsRead, Permission = false)]
    public Task<SystemLogDto> GetAsync(string logId, CancellationToken ct = default)
        => operations.ExecuteAsync(this, token => SafelyAsync(async () =>
        {
            await access.RequireAsync(ManagementOperations.LogsRead, token);
            SystemLogMetadata? row;
            try { row = await reader.GetAsync(logId, token); }
            catch (DiagnosticTokenException) { throw new ManagementDiagnosticNotFoundException(); }
            return row is null ? throw new ManagementDiagnosticNotFoundException() : projection.Project(logId, row);
        }), Audit(), ct);

    private async Task<T> SafelyAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception ex) when (ex is not (OperationCanceledException or ManagementAccessDeniedException or
            DiagnosticQueryException or DiagnosticTokenException or ManagementDiagnosticNotFoundException or ManagementDiagnosticUnavailableException))
        {
            // Provider exceptions may contain signed URLs or other sensitive text. Keep only the exception type,
            // never its message/inner exception, in management logs, audit or public responses.
            logger.LogWarning("System log read failed with exception type {ExceptionType}.", ex.GetType().FullName);
            throw new ManagementDiagnosticUnavailableException();
        }
    }
    private static ServiceOperationExecutionOptions Audit()
        => new() { OperationName = ManagementOperations.LogsRead, AuditAction = ManagementOperations.LogsRead,
            EntityName = "SystemLog", PermissionEnabled = false, AuditEnabled = true };
}
