using System.Globalization;
using System.Text.Json;
using IBeam.Management.Models;
using IBeam.Management.Services.Credentials;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services.Diagnostics;

/// <summary>Bounded internal read pipeline; the subsequent API card supplies safe public DTO projection.</summary>
public sealed class ManagementSystemErrorReader(
    ISystemErrorReadRepository repository,
    IManagementDiagnosticsAccessService access,
    DiagnosticTokenCodec codec,
    IOptions<SystemErrorReaderOptions> options,
    IOptions<ManagementDiagnosticsOptions> deployment,
    TimeProvider clock,
    IServiceOperationExecutor operations) : IManagementSystemErrorReader
{
    [IBeamOperation(ManagementOperations.ErrorsRead, Permission = false)]
    public Task<SystemErrorReadPage> ReadAsync(DiagnosticQuery query, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            ArgumentNullException.ThrowIfNull(query);
            var configured = options.Value;
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
            bounded.CancelAfter(TimeSpan.FromSeconds(configured.RequestTimeoutSeconds));
            var request = bounded.Token;
            await access.RequireAsync(ManagementOperations.ErrorsRead, request);
            var context = Context();
            var cursor = query.Cursor is null ? null : codec.Unprotect<CursorState>("cursor", context, query.Cursor);
            if (cursor is not null && (cursor.Query is null || cursor.ExpiresUtc <= clock.GetUtcNow()))
                throw new DiagnosticTokenException();
            var window = Resolve(query, cursor?.Query);
            var day = cursor?.DayUtc ?? DateOnly.FromDateTime(window.FromUtc.UtcDateTime);
            var last = DateOnly.FromDateTime(window.ToUtc.AddTicks(-1).UtcDateTime);
            if (day < DateOnly.FromDateTime(window.FromUtc.UtcDateTime) || day > last ||
                cursor?.ProviderContinuation?.Length > 8192) throw new DiagnosticTokenException();
            var continuation = cursor?.ProviderContinuation;
            var expires = cursor?.ExpiresUtc ?? clock.GetUtcNow().AddMinutes(configured.TokenLifetimeMinutes);
            var items = new List<SystemErrorReadItem>();
            var scanned = 0;
            var requests = 0;
            while (day <= last && items.Count < window.PageSize &&
                   scanned < configured.ScanBudgetPerPage && requests < configured.MaximumStorageRequestsPerPage)
            {
                request.ThrowIfCancellationRequested();
                // Recheck immediately before every repository query, including subsequent provider pages.
                await access.RequireAsync(ManagementOperations.ErrorsRead, request);
                var take = Math.Min(window.PageSize - items.Count, configured.ScanBudgetPerPage - scanned);
                var batch = await ReadStorageAsync(() => repository.ReadPartitionAsync(
                    new SystemErrorPartitionQuery(day, window.FromUtc, window.ToUtc, take, continuation), request));
                requests++;
                if (batch.ScannedCount < 0 || batch.ScannedCount > take || batch.Records.Count > batch.ScannedCount)
                    throw new InvalidOperationException("Diagnostic storage exceeded its requested page bound.");
                scanned += batch.ScannedCount;
                foreach (var row in batch.Records)
                {
                    if (!Within(row, window) || row.PartitionKey != Partition(day) || !MatchesSource(row)) continue;
                    var id = codec.Protect("record", context, new RecordState(window, row.PartitionKey, row.RowKey, expires));
                    items.Add(new SystemErrorReadItem(id, row));
                }
                if (batch.ContinuationToken is null)
                {
                    day = day.AddDays(1);
                    continuation = null;
                }
                else
                {
                    if (batch.ContinuationToken == continuation)
                        throw new InvalidOperationException("Diagnostic storage did not advance its continuation.");
                    continuation = batch.ContinuationToken;
                }
            }
            var next = day > last ? null : codec.Protect("cursor", context, new CursorState(window, day, continuation, expires));
            return new SystemErrorReadPage(items.AsReadOnly(), window, next, scanned, requests);
        }, Audit(), ct);

    [IBeamOperation(ManagementOperations.ErrorsRead, Permission = false)]
    public Task<SystemErrorRecord?> GetAsync(string recordId, DiagnosticQuery? originalQuery = null, CancellationToken ct = default)
        => operations.ExecuteAsync<SystemErrorRecord?>(this, async token =>
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
            bounded.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));
            var request = bounded.Token;
            await access.RequireAsync(ManagementOperations.ErrorsRead, request);
            var state = codec.Unprotect<RecordState>("record", Context(), recordId);
            if (state.Query is null || state.ExpiresUtc <= clock.GetUtcNow() || originalQuery?.Cursor is not null)
                throw new DiagnosticTokenException();
            var window = Resolve(originalQuery ?? new DiagnosticQuery(), state.Query);
            var row = await ReadStorageAsync(() => repository.GetAsync(state.PartitionKey, state.RowKey, request));
            return row is not null && row.PartitionKey == state.PartitionKey && row.RowKey == state.RowKey &&
                Within(row, window) && MatchesSource(row) ? row : null;
        }, Audit(), ct);

    private DiagnosticQueryWindow Resolve(DiagnosticQuery query, DiagnosticQueryWindow? saved)
    {
        var configured = options.Value;
        DiagnosticQueryWindow window;
        try
        {
            var to = query.ToUtc?.ToUniversalTime() ?? saved?.ToUtc ?? clock.GetUtcNow();
            var from = query.FromUtc?.ToUniversalTime() ?? saved?.FromUtc ?? to.AddHours(-configured.DefaultWindowHours);
            var size = query.PageSize ?? saved?.PageSize ?? configured.DefaultPageSize;
            window = new DiagnosticQueryWindow(from, to, size);
            if (from >= to || to > clock.GetUtcNow().AddMinutes(5) || to - from > TimeSpan.FromHours(configured.MaximumWindowHours) ||
                size < 1 || size > configured.MaximumPageSize || saved is not null && window != saved)
                throw new DiagnosticQueryException();
        }
        catch (ArgumentOutOfRangeException) { throw new DiagnosticQueryException(); }
        return window;
    }

    private string Context()
    {
        if (!options.Value.IsValid() || string.IsNullOrWhiteSpace(repository.SourceBinding) ||
            string.IsNullOrWhiteSpace(deployment.Value.ApplicationKey) || string.IsNullOrWhiteSpace(deployment.Value.Environment))
            throw new InvalidOperationException("Explicit diagnostic source isolation and deployment configuration are required.");
        return JsonSerializer.Serialize(new[]
        {
            "system-errors", deployment.Value.ApplicationKey, deployment.Value.Environment,
            repository.SourceBinding, options.Value.SourceMode.ToString()
        });
    }

    private bool MatchesSource(SystemErrorRecord row)
    {
        var target = deployment.Value;
        if (row.ManagementApplicationKey is null && row.ManagementEnvironment is null)
            return options.Value.SourceMode == DiagnosticSourceMode.DedicatedDeployment && options.Value.DedicatedSourceConfirmed;
        return row.ManagementApplicationKey == target.ApplicationKey && row.ManagementEnvironment == target.Environment;
    }
    private static bool Within(SystemErrorRecord row, DiagnosticQueryWindow window)
        => row.OccurredAtUtc >= window.FromUtc && row.OccurredAtUtc < window.ToUtc;
    private static string Partition(DateOnly day) => "ERR|" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    private static async Task<T> ReadStorageAsync<T>(Func<Task<T>> read)
    {
        try { return await read(); }
        catch (OperationCanceledException) { throw; }
        catch (ManagementDiagnosticUnavailableException) { throw; }
        // Translate before the operation executor audits an unsafe provider exception message.
        catch (Exception) { throw new ManagementDiagnosticUnavailableException(); }
    }
    private static ServiceOperationExecutionOptions Audit()
        => new() { OperationName = ManagementOperations.ErrorsRead, AuditAction = ManagementOperations.ErrorsRead,
            EntityName = "SystemError", PermissionEnabled = false, AuditEnabled = true };

    public sealed record CursorState(DiagnosticQueryWindow Query, DateOnly DayUtc, string? ProviderContinuation, DateTimeOffset ExpiresUtc);
    public sealed record RecordState(DiagnosticQueryWindow Query, string PartitionKey, string RowKey, DateTimeOffset ExpiresUtc);
}
