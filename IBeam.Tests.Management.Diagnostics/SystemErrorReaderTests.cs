using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;

namespace IBeam.Tests.Management.Diagnostics;

[TestClass]
public sealed class SystemErrorReaderTests
{
    [TestMethod]
    public async Task DefaultWindowIs24HoursAndUtcDatePartitionsAreExact()
    {
        var f = new ErrorReaderFixture();
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-25)));
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-24)));
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-2)));
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now));
        var page = await f.Reader().ReadAsync(new DiagnosticQuery());
        Assert.AreEqual(ErrorReaderFixture.Now.AddHours(-24), page.Query.FromUtc);
        Assert.AreEqual(ErrorReaderFixture.Now, page.Query.ToUtc);
        Assert.HasCount(2, page.Items);
        CollectionAssert.AreEqual(new[] { new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 16) }, f.Repository.Queries.Select(x => x.DayUtc).ToArray());
        Assert.IsNull(page.NextCursor);
        var json = JsonSerializer.Serialize(page);
        Assert.IsFalse(json.Contains("untrusted", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("ERR|", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PaginationKeepsFrozenWindowAndDoesNotDuplicateOrSkipRows()
    {
        var f = new ErrorReaderFixture();
        for (var i = 0; i < 6; i++) f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-10).AddMinutes(i)));
        var reader = f.Reader();
        var first = await reader.ReadAsync(new DiagnosticQuery { PageSize = 2 });
        var second = await reader.ReadAsync(new DiagnosticQuery { Cursor = first.NextCursor });
        var third = await reader.ReadAsync(new DiagnosticQuery { Cursor = second.NextCursor });
        Assert.HasCount(2, first.Items); Assert.HasCount(2, second.Items); Assert.HasCount(2, third.Items);
        Assert.AreEqual(first.Query, second.Query);
        Assert.IsNull(third.NextCursor);
        Assert.AreEqual(6, first.Items.Concat(second.Items).Concat(third.Items).Select(x => x.Record.RowKey).Distinct().Count());
        var detail = await reader.GetAsync(first.Items[0].Id);
        Assert.AreEqual(first.Items[0].Record.RowKey, detail!.RowKey);
        f.Repository.Rows.Clear();
        Assert.IsNull(await reader.GetAsync(first.Items[0].Id));
    }

    [TestMethod]
    public async Task SharedSourceSuppressesUntaggedPartialAndOtherDeploymentRowsWithinScanBudget()
    {
        var f = new ErrorReaderFixture();
        f.Options.SourceMode = DiagnosticSourceMode.TaggedShared; f.Options.TrustedTagsConfirmed = true;
        f.Options.DefaultPageSize = 1; f.Options.MaximumPageSize = 1; f.Options.ScanBudgetPerPage = 2;
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-8)));
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-7), "qurvia"));
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-6), "bindry", "Production"));
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-5), "qurvia", "Test"));
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-4), "qurvia", "Production"));
        var reader = f.Reader();
        var first = await reader.ReadAsync(new DiagnosticQuery());
        Assert.HasCount(0, first.Items); Assert.IsNotNull(first.NextCursor); Assert.AreEqual(2, first.ScannedCount);
        var second = await reader.ReadAsync(new DiagnosticQuery { Cursor = first.NextCursor });
        Assert.HasCount(0, second.Items); Assert.IsNotNull(second.NextCursor); Assert.AreEqual(2, second.ScannedCount);
        var third = await reader.ReadAsync(new DiagnosticQuery { Cursor = second.NextCursor });
        Assert.HasCount(1, third.Items); Assert.IsNull(third.NextCursor);
        Assert.AreEqual("qurvia", third.Items[0].Record.ManagementApplicationKey);
    }

    [TestMethod]
    public async Task DedicatedSourceStillRejectsExplicitForeignTagsAndDetailRechecksTags()
    {
        var f = new ErrorReaderFixture();
        var row = ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-2));
        f.Repository.Rows.Add(row);
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1), "medinsights", "Production"));
        var page = await f.Reader().ReadAsync(new DiagnosticQuery());
        Assert.HasCount(1, page.Items);
        f.Repository.Rows[0] = row with { ManagementApplicationKey = "bindry", ManagementEnvironment = "Production" };
        Assert.IsNull(await f.Reader().GetAsync(page.Items[0].Id));
    }

    [TestMethod]
    public async Task EmptyProviderPagesAndRequestBudgetHaveAResumableContinuation()
    {
        var f = new ErrorReaderFixture();
        f.Options.MaximumStorageRequestsPerPage = 1;
        f.Repository.EmptyProviderPage = true;
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-2)));
        var page = await f.Reader().ReadAsync(new DiagnosticQuery());
        Assert.HasCount(0, page.Items); Assert.AreEqual(1, page.StorageRequests); Assert.IsNotNull(page.NextCursor);
        // Continue across the empty first partition/provider page without turning the request into an unbounded loop.
        for(var i = 0; i < 4 && page.Items.Count == 0 && page.NextCursor is not null; i++)
            page = await f.Reader().ReadAsync(new DiagnosticQuery { Cursor = page.NextCursor });
        Assert.HasCount(1, page.Items);
        Assert.AreEqual(1, page.StorageRequests);
    }

    [TestMethod]
    [DataRow("size")]
    [DataRow("window")]
    [DataRow("order")]
    [DataRow("min-default")]
    [DataRow("future")]
    public async Task InvalidBoundsFailBeforeStorage(string change)
    {
        var f = new ErrorReaderFixture();
        var query = change switch
        {
            "size" => new DiagnosticQuery { PageSize = 1001 },
            "min-default" => new DiagnosticQuery { ToUtc = DateTimeOffset.MinValue },
            "future" => new DiagnosticQuery { FromUtc = ErrorReaderFixture.Now.AddHours(1), ToUtc = ErrorReaderFixture.Now.AddHours(2) },
            "window" => new DiagnosticQuery { FromUtc = ErrorReaderFixture.Now.AddDays(-8), ToUtc = ErrorReaderFixture.Now },
            _ => new DiagnosticQuery { FromUtc = ErrorReaderFixture.Now, ToUtc = ErrorReaderFixture.Now }
        };
        await Assert.ThrowsAsync<DiagnosticQueryException>(() => f.Reader().ReadAsync(query));
        Assert.HasCount(0, f.Repository.Queries);
    }

    [TestMethod]
    public async Task AccessIsRequiredBeforeEachProviderRequestAndForDetail()
    {
        var f = new ErrorReaderFixture();
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-2)));
        var page = await f.Reader().ReadAsync(new DiagnosticQuery());
        f.Authorization.Deny = true;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Reader().GetAsync(page.Items[0].Id));
        Assert.AreEqual(0, f.Repository.Gets);
        f.Authorization.Deny = false;
        f.Repository.Queries.Clear();
        f.Repository.OnRead = () => f.Authorization.Deny = true;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Reader().ReadAsync(new DiagnosticQuery()));
        Assert.HasCount(1, f.Repository.Queries);
    }

    [TestMethod]
    public async Task CancellationAndConfiguredDeadlineAreHonored()
    {
        var f = new ErrorReaderFixture();
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Reader().ReadAsync(new DiagnosticQuery(), canceled.Token));
        f.Options.RequestTimeoutSeconds = 1;
        f.Repository.DelayUntilCancellation = true;
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Reader().ReadAsync(new DiagnosticQuery()));
    }
}
