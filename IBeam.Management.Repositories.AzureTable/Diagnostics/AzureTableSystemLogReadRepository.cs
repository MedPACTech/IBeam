using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using IBeam.Management.Models;

namespace IBeam.Management.Repositories.AzureTable.Diagnostics;

/// <summary>
/// Read-only adapter for the existing SystemLogs format. Queries only SYSTEM|DAY|yyyyMMdd partitions and Category
/// "System"; tenant partitions, EntityChange and SelectRollup rows are never returned. Message, Detail, actor/device/IP
/// and before/after/original/transformed JSON columns are never selected. No writes, table creation, Identity or services.
/// </summary>
public sealed class AzureTableSystemLogReadRepository : ISystemLogReadRepository
{
    private const string SystemCategory = "System";
    private readonly TableClient _table;
    private static readonly string[] Columns =
    [
        "PartitionKey", "RowKey", "OccurredAtUtc", "Category", "IsSelectRollup", "Source", "Level", "TraceId",
        "ManagementApplicationKey", "ManagementEnvironment"
    ];
    public string SourceBinding { get; }
    public AzureTableSystemLogReadRepository(TableClient table)
    {
        _table = table;
        SourceBinding = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(table.Uri.GetLeftPart(UriPartial.Path))));
    }

    public async Task<SystemLogPartitionPage> ReadPartitionAsync(SystemLogPartitionQuery query, CancellationToken ct = default)
    {
        if (query.Take is < 1 or > 1000 || query.FromUtc >= query.ToUtc)
            throw new ArgumentException("A bounded diagnostic page is required.");
        var partition = "SYSTEM|DAY|" + query.DayUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var clauses = new List<string>
        {
            TableClient.CreateQueryFilter(
                $"PartitionKey eq {partition} and Category eq {SystemCategory} and OccurredAtUtc ge {query.FromUtc} and OccurredAtUtc lt {query.ToUtc}")
        };
        if (query.Source is not null) clauses.Add(TableClient.CreateQueryFilter($"Source eq {query.Source}"));
        if (query.Level is not null) clauses.Add(TableClient.CreateQueryFilter($"Level eq {query.Level}"));
        if (query.TraceId is not null) clauses.Add(TableClient.CreateQueryFilter($"TraceId eq {query.TraceId}"));
        var filter = string.Join(" and ", clauses);
        var pages = _table.QueryAsync<TableEntity>(filter, query.Take, Columns, ct).AsPages(query.ContinuationToken, query.Take);
        await using var iterator = pages.GetAsyncEnumerator(ct);
        if (!await iterator.MoveNextAsync()) return new SystemLogPartitionPage([], 0, null);
        var page = iterator.Current;
        var records = page.Values.Select(Map).OfType<SystemLogMetadata>().ToArray();
        return new SystemLogPartitionPage(Array.AsReadOnly(records), page.Values.Count, page.ContinuationToken);
    }

    public async Task<SystemLogMetadata?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(partitionKey ?? "", "^SYSTEM\\|DAY\\|[0-9]{8}$") ||
            !DateOnly.TryParseExact(partitionKey![11..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            !Regex.IsMatch(rowKey ?? "", "^[0-9]{13}\\|[0-9a-f]{32}$"))
            throw new DiagnosticTokenException();
        var response = await _table.GetEntityIfExistsAsync<TableEntity>(partitionKey, rowKey!, Columns, ct);
        return response.HasValue ? Map(response.Value!) : null;
    }

    private static SystemLogMetadata? Map(TableEntity row)
    {
        // Event rows only: rollup keys, other partitions and anything not explicitly an operational System row are dropped.
        if (!Regex.IsMatch(row.PartitionKey, "^SYSTEM\\|DAY\\|[0-9]{8}$") ||
            !Regex.IsMatch(row.RowKey, "^[0-9]{13}\\|[0-9a-f]{32}$") ||
            Text(row, "Category") != SystemCategory ||
            row.TryGetValue("IsSelectRollup", out var rollup) && rollup is not (null or false)) return null;
        DateTimeOffset? occurred;
        try { occurred = row.GetDateTimeOffset("OccurredAtUtc"); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or ArgumentException) { return null; }
        if (occurred is null) return null;
        return new SystemLogMetadata
        {
            PartitionKey = row.PartitionKey, RowKey = row.RowKey, OccurredAtUtc = occurred.Value,
            Source = Text(row, "Source") ?? "", Level = Text(row, "Level") ?? "", TraceId = Text(row, "TraceId") ?? "",
            ManagementApplicationKey = row.ContainsKey("ManagementApplicationKey") ? Text(row, "ManagementApplicationKey") ?? "" : null,
            ManagementEnvironment = row.ContainsKey("ManagementEnvironment") ? Text(row, "ManagementEnvironment") ?? "" : null
        };
    }
    private static string? Text(TableEntity row, string key)
        => row.TryGetValue(key, out var value) && value is string text ? text : null;
}
