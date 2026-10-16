using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Diagnostics;

[TestClass]
public sealed class SystemLogReaderTests
{
    [TestMethod]
    public async Task DefaultWindowIs24HoursAndOnlySystemDayPartitionsAreQueried()
    {
        var f = new LogReaderFixture();
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-25)));
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-24)));
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-2)));
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now));
        var page = await f.Reader().ReadAsync(new SystemLogQuery());
        Assert.AreEqual(LogReaderFixture.Now.AddHours(-24), page.Query.FromUtc);
        Assert.AreEqual(LogReaderFixture.Now, page.Query.ToUtc);
        Assert.HasCount(2, page.Items);
        CollectionAssert.AreEqual(new[] { new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 16) }, f.Repository.Queries.Select(x => x.DayUtc).ToArray());
        Assert.IsNull(page.NextCursor);
        var json = JsonSerializer.Serialize(page);
        Assert.IsFalse(json.Contains("SYSTEM|", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("trace-1", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FiltersArePushedToStorageAndFrozenIntoTheCursor()
    {
        var f = new LogReaderFixture();
        for (var i = 0; i < 4; i++) f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-3).AddMinutes(i), trace: "wanted"));
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-3).AddMinutes(10), trace: "other"));
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-3).AddMinutes(11), level: "Critical", trace: "wanted"));
        var reader = f.Reader();
        var first = await reader.ReadAsync(new SystemLogQuery { PageSize = 2, Level = "Error", Source = "RequestPipeline", TraceId = "wanted" });
        var second = await reader.ReadAsync(new SystemLogQuery { Cursor = first.NextCursor });
        Assert.HasCount(2, first.Items); Assert.HasCount(2, second.Items);
        Assert.IsTrue(f.Repository.Queries.All(q => q is { Level: "Error", Source: "RequestPipeline", TraceId: "wanted" }));
        Assert.AreEqual(first.Query, second.Query);
        Assert.AreEqual(4, first.Items.Concat(second.Items).Select(x => x.Record.RowKey).Distinct().Count());
        // Restating the same filter is allowed; changing or widening it is not.
        await reader.ReadAsync(new SystemLogQuery { Cursor = first.NextCursor, Level = "Error" });
        await Assert.ThrowsAsync<DiagnosticQueryException>(() => reader.ReadAsync(new SystemLogQuery { Cursor = first.NextCursor, Level = "Critical" }));
        await Assert.ThrowsAsync<DiagnosticQueryException>(() => reader.ReadAsync(new SystemLogQuery { Cursor = first.NextCursor, TraceId = "other" }));
    }

    [TestMethod]
    public async Task ReaderRechecksFiltersEvenWhenStorageIgnoresThem()
    {
        var f = new LogReaderFixture();
        f.Repository.IgnoreFilters = true;
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-2), trace: "wanted"));
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1), trace: "other", level: "Warning"));
        var page = await f.Reader().ReadAsync(new SystemLogQuery { TraceId = "wanted", Level = "Error" });
        Assert.HasCount(1, page.Items);
        Assert.AreEqual("wanted", page.Items[0].Record.TraceId);
    }

    [TestMethod]
    [DataRow("source", "UnapprovedSource")]
    [DataRow("source", "System")]
    [DataRow("level", "error")]
    [DataRow("level", "Fatal")]
    [DataRow("trace", "has space")]
    [DataRow("trace", "quote'injection")]
    [DataRow("trace", "")]
    public async Task UnapprovedFilterValuesAreRejectedBeforeStorage(string field, string value)
    {
        var f = new LogReaderFixture();
        var query = field switch
        {
            "source" => new SystemLogQuery { Source = value },
            "level" => new SystemLogQuery { Level = value },
            _ => new SystemLogQuery { TraceId = value }
        };
        await Assert.ThrowsAsync<DiagnosticQueryException>(() => f.Reader().ReadAsync(query));
        Assert.HasCount(0, f.Repository.Queries);
    }

    [TestMethod]
    public async Task HostApprovedSourcesCanBeFiltered()
    {
        var f = new LogReaderFixture();
        f.Options.AllowedSources = ["Qurvia.Scheduler"];
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1), source: "Qurvia.Scheduler"));
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-2)));
        var page = await f.Reader().ReadAsync(new SystemLogQuery { Source = "Qurvia.Scheduler" });
        Assert.HasCount(1, page.Items);
    }

    [TestMethod]
    public async Task SharedSourceSuppressesUntaggedPartialAndOtherDeploymentRows()
    {
        var f = new LogReaderFixture();
        f.Options.SourceMode = DiagnosticSourceMode.TaggedShared; f.Options.TrustedTagsConfirmed = true;
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-8)));
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-7), "qurvia"));
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-6), "bindry", "Production"));
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-5), "qurvia", "Test"));
        var kept = LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-4), "qurvia", "Production");
        f.Repository.Rows.Add(kept);
        var page = await f.Reader().ReadAsync(new SystemLogQuery());
        Assert.HasCount(1, page.Items);
        Assert.AreEqual(kept.RowKey, page.Items[0].Record.RowKey);
    }

    [TestMethod]
    public async Task ScanAndRequestBudgetsBoundEachPage()
    {
        var f = new LogReaderFixture();
        f.Options.DefaultPageSize = 2; f.Options.MaximumPageSize = 2; f.Options.ScanBudgetPerPage = 2;
        f.Options.SourceMode = DiagnosticSourceMode.TaggedShared; f.Options.TrustedTagsConfirmed = true;
        for (var i = 0; i < 6; i++) f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1).AddMinutes(i)));
        var page = await f.Reader().ReadAsync(new SystemLogQuery());
        Assert.HasCount(0, page.Items);
        Assert.AreEqual(2, page.ScannedCount);
        Assert.IsNotNull(page.NextCursor);
    }

    [TestMethod]
    public async Task AccessIsRecheckedBeforeEveryStorageReadAndLogsReadIsRequired()
    {
        var f = new LogReaderFixture();
        f.Options.DefaultPageSize = 1; f.Options.MaximumPageSize = 1; f.Options.ScanBudgetPerPage = 1;
        f.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-30)));
        var page = await f.Reader().ReadAsync(new SystemLogQuery { FromUtc = LogReaderFixture.Now.AddHours(-48) });
        var queries = f.Repository.Queries.Count;
        Assert.IsGreaterThan(1, queries);
        Assert.AreEqual(queries + 1, f.Authorization.Calls);
        f.Authorization.Deny = true;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Reader().ReadAsync(new SystemLogQuery { Cursor = page.NextCursor }));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Reader().GetAsync("anything"));
        Assert.HasCount(queries, f.Repository.Queries);
        Assert.AreEqual(0, f.Repository.Gets);
    }

    [TestMethod]
    public async Task DetailUsesTheProtectedIdAndRejectsTamperedExpiredAndForeignSources()
    {
        var f = new LogReaderFixture();
        var row = LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1));
        f.Repository.Rows.Add(row);
        var reader = f.Reader();
        var id = (await reader.ReadAsync(new SystemLogQuery())).Items[0].Id;
        Assert.AreEqual(row.RowKey, (await reader.GetAsync(id))!.RowKey);
        Assert.IsFalse(id.Contains(row.RowKey, StringComparison.Ordinal));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => reader.GetAsync(id[..^2] + "AA"));
        f.Repository.SourceBinding = "another-table";
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Reader().GetAsync(id));
        f.Repository.SourceBinding = "fixture-log-source";
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Reader(deployment: new() { ApplicationKey = "qurvia", Environment = "Test" }).GetAsync(id));
        f.Time.NowUtc = LogReaderFixture.Now.AddMinutes(31);
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Reader().GetAsync(id));
    }

    [TestMethod]
    public async Task ErrorAndLogTokensCannotBeSubstitutedEvenWithTheSameKey()
    {
        var logs = new LogReaderFixture();
        logs.Repository.Rows.Add(LogReaderFixture.Row(LogReaderFixture.Now.AddHours(-1)));
        var errors = new ErrorReaderFixture();
        errors.Options.ProtectionKey = logs.Options.ProtectionKey;
        errors.Repository.SourceBinding = logs.Repository.SourceBinding;
        errors.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1)));
        var errorId = (await errors.Reader().ReadAsync(new DiagnosticQuery { PageSize = 1 })).Items[0].Id;
        var logId = (await logs.Reader().ReadAsync(new SystemLogQuery())).Items[0].Id;
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => logs.Reader().GetAsync(errorId));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => errors.Reader().GetAsync(logId));
    }

    [TestMethod]
    public async Task ProviderFailuresBecomeUnavailableAndCancellationIsHonoured()
    {
        var f = new LogReaderFixture();
        f.Repository.OnRead = () => throw new InvalidOperationException("signed-url secret");
        await Assert.ThrowsAsync<ManagementDiagnosticUnavailableException>(() => f.Reader().ReadAsync(new SystemLogQuery()));
        f.Repository.OnRead = null;
        f.Repository.DelayUntilCancellation = true;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Reader().ReadAsync(new SystemLogQuery(), cancel.Token));
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("confirmation")]
    [DataRow("key")]
    [DataRow("window")]
    [DataRow("page")]
    [DataRow("allowed-pattern")]
    [DataRow("allowed-system")]
    [DataRow("allowed-count")]
    public void UnsafeOrAmbiguousLogReaderSettingsAreRejected(string change)
    {
        var f = new LogReaderFixture();
        switch (change)
        {
            case "source": f.Options.SourceMode = DiagnosticSourceMode.Unspecified; break;
            case "confirmation": f.Options.DedicatedSourceConfirmed = false; break;
            case "key": f.Options.ProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)); break;
            case "window": f.Options.MaximumWindowHours = 1000; break;
            case "page": f.Options.MaximumPageSize = 1001; break;
            case "allowed-pattern": f.Options.AllowedSources = ["user=someone@example.com"]; break;
            case "allowed-system": f.Options.AllowedSources = ["System"]; break;
            case "allowed-count": f.Options.AllowedSources = Enumerable.Range(0, 33).Select(i => "Source" + i).ToArray(); break;
        }
        Assert.IsFalse(f.Options.IsValid());
        Assert.Throws<InvalidOperationException>(() => f.Reader());
        Assert.Throws<InvalidOperationException>(() => f.Projection());
    }

    [TestMethod]
    public void ProjectionUsesFixedCatalogNormalizedLevelAndDeploymentScopedPseudonyms()
    {
        var f = new LogReaderFixture();
        f.Options.AllowedSources = ["Qurvia.Scheduler"];
        var projection = f.Projection();
        var known = projection.Project("id", LogReaderFixture.Row(LogReaderFixture.Now, trace: "0HND11FN7L0DA:00000001"));
        Assert.AreEqual("RequestPipeline", known.Source);
        Assert.AreEqual("An unhandled request processing failure was logged.", known.Summary);
        Assert.AreEqual("Error", known.Level);
        Assert.IsTrue(known.CorrelationId!.StartsWith("c1_", StringComparison.Ordinal));
        Assert.IsFalse(known.CorrelationId.Contains("0HND11FN7L0DA", StringComparison.Ordinal));
        var allowed = projection.Project("id", LogReaderFixture.Row(LogReaderFixture.Now, source: "Qurvia.Scheduler", level: "Warning"));
        Assert.AreEqual("Qurvia.Scheduler", allowed.Source);
        Assert.AreEqual("A system log entry was recorded.", allowed.Summary);
        var unknown = projection.Project("id", LogReaderFixture.Row(LogReaderFixture.Now,
            source: "Job?token=" + SystemErrorApiFixture.Sensitive, level: "level " + SystemErrorApiFixture.Sensitive));
        Assert.AreEqual("System", unknown.Source);
        Assert.AreEqual("Unknown", unknown.Level);
        Assert.IsFalse(JsonSerializer.Serialize(unknown).Contains(SystemErrorApiFixture.Sensitive, StringComparison.Ordinal));
        var other = f.Projection(new ManagementDiagnosticsOptions { ApplicationKey = "medinsights", Environment = "Production" });
        Assert.AreNotEqual(known.CorrelationId, other.Project("id", LogReaderFixture.Row(LogReaderFixture.Now, trace: "0HND11FN7L0DA:00000001")).CorrelationId);
        Assert.IsNull(projection.Project("id", LogReaderFixture.Row(LogReaderFixture.Now, trace: "")).CorrelationId);
    }
}
