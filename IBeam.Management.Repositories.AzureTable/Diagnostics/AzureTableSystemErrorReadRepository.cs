using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using IBeam.Management.Models;

namespace IBeam.Management.Repositories.AzureTable.Diagnostics;

/// <summary>Read-only adapter for the existing SystemErrors format. No writes, table creation, Identity or services.</summary>
public sealed class AzureTableSystemErrorReadRepository : ISystemErrorReadRepository
{
    private readonly TableClient _table;
    private static readonly string[] Columns =
        ["PartitionKey", "RowKey", "OccurredAtUtc", "Source", "TraceId", "ManagementApplicationKey", "ManagementEnvironment"];
    private static readonly string[] RawColumns = Columns.Concat<string>(["Message", "Exception", "Path", "Method"]).ToArray();
    public string SourceBinding { get; }
    public AzureTableSystemErrorReadRepository(TableClient table)
    {
        _table = table;
        SourceBinding = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(table.Uri.GetLeftPart(UriPartial.Path))));
    }

    public async Task<SystemErrorPartitionPage> ReadPartitionAsync(SystemErrorPartitionQuery query, CancellationToken ct = default)
    {
        if (query.Take is < 1 or > 1000 || query.FromUtc >= query.ToUtc)
            throw new ArgumentException("A bounded diagnostic page is required.");
        var partition = "ERR|" + query.DayUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {partition} and OccurredAtUtc ge {query.FromUtc} and OccurredAtUtc lt {query.ToUtc}");
        var pages = _table.QueryAsync<TableEntity>(filter, query.Take, query.IncludeUnsanitized ? RawColumns : Columns, ct).AsPages(query.ContinuationToken, query.Take);
        await using var iterator = pages.GetAsyncEnumerator(ct);
        if (!await iterator.MoveNextAsync()) return new SystemErrorPartitionPage([], 0, null);
        var page = iterator.Current;
        var records = page.Values.Select(x => Map(x, query.IncludeUnsanitized)).OfType<SystemErrorRecord>().ToArray();
        return new SystemErrorPartitionPage(Array.AsReadOnly(records), page.Values.Count, page.ContinuationToken);
    }

    public Task<SystemErrorRecord?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
        => GetCoreAsync(partitionKey, rowKey, false, ct);
    public Task<SystemErrorRecord?> GetUnsanitizedAsync(string partitionKey, string rowKey, CancellationToken ct = default)
        => GetCoreAsync(partitionKey, rowKey, true, ct);
    private async Task<SystemErrorRecord?> GetCoreAsync(string partitionKey, string rowKey, bool includeUnsanitized, CancellationToken ct)
    {
        if (!Regex.IsMatch(partitionKey ?? "", "^ERR\\|[0-9]{8}$") ||
            !DateOnly.TryParseExact(partitionKey![4..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            !Regex.IsMatch(rowKey ?? "", "^[0-9]{9}\\|[0-9a-f]{32}$"))
            throw new DiagnosticTokenException();
        var response = await _table.GetEntityIfExistsAsync<TableEntity>(partitionKey, rowKey!, includeUnsanitized ? RawColumns : Columns, ct);
        return response.HasValue ? Map(response.Value!, includeUnsanitized) : null;
    }

    private static SystemErrorRecord? Map(TableEntity row, bool includeUnsanitized)
    {
        if (!Regex.IsMatch(row.RowKey, "^[0-9]{9}\\|[0-9a-f]{32}$")) return null;
        DateTimeOffset? occurred;
        try { occurred = row.GetDateTimeOffset("OccurredAtUtc"); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or ArgumentException) { return null; }
        if (occurred is null) return null;
        return new SystemErrorRecord
        {
            PartitionKey = row.PartitionKey, RowKey = row.RowKey, OccurredAtUtc = occurred.Value,
            Source = Text(row, "Source") ?? "", TraceId = Text(row, "TraceId") ?? "",
            Message = includeUnsanitized ? Text(row, "Message") : null,
            Exception = includeUnsanitized ? Text(row, "Exception") : null,
            Path = includeUnsanitized ? Text(row, "Path") : null,
            Method = includeUnsanitized ? Text(row, "Method") : null,
            ManagementApplicationKey = row.ContainsKey("ManagementApplicationKey") ? Text(row, "ManagementApplicationKey") ?? "" : null,
            ManagementEnvironment = row.ContainsKey("ManagementEnvironment") ? Text(row, "ManagementEnvironment") ?? "" : null
        };
    }
    private static string? Text(TableEntity row, string key)
        => row.TryGetValue(key, out var value) && value is string text ? text : null;
}
