using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using IBeam.Management.Models;
using IBeam.Management.Services.Credentials;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services.Diagnostics;

/// <summary>Bounded internal read pipeline for operational system logs; SystemLogReaderService supplies the public projection.</summary>
public sealed class ManagementSystemLogReader(
    ISystemLogReadRepository repository,
    IManagementDiagnosticsAccessService access,
    SystemLogTokenCodec codec,
    IOptions<SystemLogReaderOptions> options,
    IOptions<ManagementDiagnosticsOptions> deployment,
    TimeProvider clock,
    IServiceOperationExecutor operations) : IManagementSystemLogReader
{
    [IBeamOperation(ManagementOperations.LogsRead, Permission = false)]
    public Task<SystemLogReadPage> ReadAsync(SystemLogQuery query, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            ArgumentNullException.ThrowIfNull(query);
            var configured = options.Value;
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
            bounded.CancelAfter(TimeSpan.FromSeconds(configured.RequestTimeoutSeconds));
            var request = bounded.Token;
            await access.RequireAsync(ManagementOperations.LogsRead, request);
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
            var items = new List<SystemLogReadItem>();
            var scanned = 0;
            var requests = 0;
            while (day <= last && items.Count < window.PageSize &&
                   scanned < configured.ScanBudgetPerPage && requests < configured.MaximumStorageRequestsPerPage)
            {
                request.ThrowIfCancellationRequested();
                // Recheck immediately before every repository query, including subsequent provider pages.
                await access.RequireAsync(ManagementOperations.LogsRead, request);
                var take = Math.Min(window.PageSize - items.Count, configured.ScanBudgetPerPage - scanned);
                var batch = await ReadStorageAsync(() => repository.ReadPartitionAsync(new SystemLogPartitionQuery(
                    day, window.FromUtc, window.ToUtc, take, continuation, window.Source, window.Level, window.TraceId), request));
                requests++;
                if (batch.ScannedCount < 0 || batch.ScannedCount > take || batch.Records.Count > batch.ScannedCount)
                    throw new InvalidOperationException("Diagnostic storage exceeded its requested page bound.");
                scanned += batch.ScannedCount;
                foreach (var row in batch.Records)
                {
                    if (!Within(row, window) || row.PartitionKey != Partition(day) || !MatchesSource(row) || !MatchesFilters(row, window)) continue;
                    var id = codec.Protect("record", context, new RecordState(window, row.PartitionKey, row.RowKey, expires));
                    items.Add(new SystemLogReadItem(id, row));
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
            return new SystemLogReadPage(items.AsReadOnly(), window, next, scanned, requests);
        }, Audit(), ct);

    [IBeamOperation(ManagementOperations.LogsRead, Permission = false)]
    public Task<SystemLogMetadata?> GetAsync(string recordId, CancellationToken ct = default)
        => operations.ExecuteAsync<SystemLogMetadata?>(this, async token =>
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
            bounded.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));
            var request = bounded.Token;
            await access.RequireAsync(ManagementOperations.LogsRead, request);
            var state = codec.Unprotect<RecordState>("record", Context(), recordId);
            if (state.Query is null || state.ExpiresUtc <= clock.GetUtcNow()) throw new DiagnosticTokenException();
            var window = Resolve(new SystemLogQuery(), state.Query);
            var row = await ReadStorageAsync(() => repository.GetAsync(state.PartitionKey, state.RowKey, request));
            return row is not null && row.PartitionKey == state.PartitionKey && row.RowKey == state.RowKey &&
                Within(row, window) && MatchesSource(row) && MatchesFilters(row, window) ? row : null;
        }, Audit(), ct);

    private SystemLogQueryWindow Resolve(SystemLogQuery query, SystemLogQueryWindow? saved)
    {
        var configured = options.Value;
        SystemLogQueryWindow window;
        try
        {
            var to = query.ToUtc?.ToUniversalTime() ?? saved?.ToUtc ?? clock.GetUtcNow();
            var from = query.FromUtc?.ToUniversalTime() ?? saved?.FromUtc ?? to.AddHours(-configured.DefaultWindowHours);
            var size = query.PageSize ?? saved?.PageSize ?? configured.DefaultPageSize;
            window = new SystemLogQueryWindow(from, to, size,
                query.Source ?? saved?.Source, query.Level ?? saved?.Level, query.TraceId ?? saved?.TraceId);
            if (from >= to || to > clock.GetUtcNow().AddMinutes(5) || to - from > TimeSpan.FromHours(configured.MaximumWindowHours) ||
                size < 1 || size > configured.MaximumPageSize || saved is not null && window != saved ||
                window.Source is not null && !SystemLogSources.IsApproved(window.Source, configured) ||
                window.Level is not null && !SystemLogLevels.All.Contains(window.Level, StringComparer.Ordinal) ||
                window.TraceId is not null && !Regex.IsMatch(window.TraceId, "^[A-Za-z0-9:._|-]{1,128}$"))
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
            "system-logs", deployment.Value.ApplicationKey, deployment.Value.Environment,
            repository.SourceBinding, options.Value.SourceMode.ToString()
        });
    }

    private bool MatchesSource(SystemLogMetadata row)
    {
        var target = deployment.Value;
        if (row.ManagementApplicationKey is null && row.ManagementEnvironment is null)
            return options.Value.SourceMode == DiagnosticSourceMode.DedicatedDeployment && options.Value.DedicatedSourceConfirmed;
        return row.ManagementApplicationKey == target.ApplicationKey && row.ManagementEnvironment == target.Environment;
    }
    private static bool MatchesFilters(SystemLogMetadata row, SystemLogQueryWindow window)
        => (window.Source is null || row.Source == window.Source) &&
           (window.Level is null || row.Level == window.Level) &&
           (window.TraceId is null || row.TraceId == window.TraceId);
    private static bool Within(SystemLogMetadata row, SystemLogQueryWindow window)
        => row.OccurredAtUtc >= window.FromUtc && row.OccurredAtUtc < window.ToUtc;
    private static string Partition(DateOnly day) => "SYSTEM|DAY|" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    private static async Task<T> ReadStorageAsync<T>(Func<Task<T>> read)
    {
        try { return await read(); }
        catch (OperationCanceledException) { throw; }
        catch (ManagementDiagnosticUnavailableException) { throw; }
        // Translate before the operation executor audits an unsafe provider exception message.
        catch (Exception) { throw new ManagementDiagnosticUnavailableException(); }
    }
    private static ServiceOperationExecutionOptions Audit()
        => new() { OperationName = ManagementOperations.LogsRead, AuditAction = ManagementOperations.LogsRead,
            EntityName = "SystemLog", PermissionEnabled = false, AuditEnabled = true };

    public sealed record CursorState(SystemLogQueryWindow Query, DateOnly DayUtc, string? ProviderContinuation, DateTimeOffset ExpiresUtc);
    public sealed record RecordState(SystemLogQueryWindow Query, string PartitionKey, string RowKey, DateTimeOffset ExpiresUtc);
}
