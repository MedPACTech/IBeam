using IBeam.Management.Models;
using IBeam.Management.Services.Credentials;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace IBeam.Management.Services.Diagnostics;

public sealed class SystemErrorReaderService(
    IManagementSystemErrorReader reader,
    IManagementDiagnosticsAccessService access,
    SystemErrorProjection projection,
    IServiceOperationExecutor operations,
    ILogger<SystemErrorReaderService> logger) : ISystemErrorReaderService
{
    [IBeamOperation(ManagementOperations.ErrorsRead, Permission = false)]
    public Task<SystemErrorPage> ListAsync(DiagnosticQuery query, CancellationToken ct = default)
        => operations.ExecuteAsync(this, token => SafelyAsync(async () =>
        {
            await access.RequireAsync(ManagementOperations.ErrorsRead, token);
            var page = await reader.ReadAsync(query, token);
            return new SystemErrorPage(Array.AsReadOnly(page.Items.Select(x => projection.Project(x.Id, x.Record)).ToArray()),
                page.Query.PageSize, page.NextCursor);
        }), Audit(), ct);

    [IBeamOperation(ManagementOperations.ErrorsRead, Permission = false)]
    public Task<SystemErrorDto> GetAsync(string errorId, CancellationToken ct = default)
        => operations.ExecuteAsync(this, token => SafelyAsync(async () =>
        {
            await access.RequireAsync(ManagementOperations.ErrorsRead, token);
            SystemErrorRecord? row;
            try { row = await reader.GetAsync(errorId, ct: token); }
            catch (DiagnosticTokenException) { throw new ManagementDiagnosticNotFoundException(); }
            return row is null ? throw new ManagementDiagnosticNotFoundException() : projection.Project(errorId, row);
        }), Audit(), ct);

    private async Task<T> SafelyAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception ex) when (ex is not (OperationCanceledException or ManagementAccessDeniedException or
            DiagnosticQueryException or DiagnosticTokenException or ManagementDiagnosticNotFoundException or ManagementDiagnosticUnavailableException))
        {
            // Provider exceptions may contain signed URLs or other sensitive text. Keep only the exception type,
            // never its message/inner exception, in management logs, audit or public responses.
            logger.LogWarning("System error read failed with exception type {ExceptionType}.", ex.GetType().FullName);
            throw new ManagementDiagnosticUnavailableException();
        }
    }
    private static ServiceOperationExecutionOptions Audit()
        => new() { OperationName = ManagementOperations.ErrorsRead, AuditAction = ManagementOperations.ErrorsRead,
            EntityName = "SystemError", PermissionEnabled = false, AuditEnabled = true };
}
