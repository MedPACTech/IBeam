using System.Security.Cryptography;
using IBeam.Management;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Diagnostics;

[TestClass]
public sealed class DiagnosticTokenTests
{
    private static async Task<(ErrorReaderFixture Fixture, SystemErrorReadPage Page)> Setup()
    {
        var f = new ErrorReaderFixture();
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-2)));
        f.Repository.Rows.Add(ErrorReaderFixture.Row(ErrorReaderFixture.Now.AddHours(-1)));
        return (f, await f.Reader().ReadAsync(new DiagnosticQuery { PageSize = 1 }));
    }

    [TestMethod]
    [DataRow("application")]
    [DataRow("environment")]
    [DataRow("source")]
    [DataRow("mode")]
    [DataRow("key")]
    public async Task CursorsAndRecordIdsCannotMoveToAnotherContext(string change)
    {
        var (f, page) = await Setup();
        if (change == "application") f.Deployment.ApplicationKey = "bindry";
        if (change == "environment") f.Deployment.Environment = "Test";
        if (change == "source") f.Repository.SourceBinding = "other-physical-table";
        if (change == "mode") { f.Options.SourceMode = DiagnosticSourceMode.TaggedShared; f.Options.TrustedTagsConfirmed = true; }
        if (change == "key") f.Options.ProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var before = f.Repository.Queries.Count;
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Reader().ReadAsync(new DiagnosticQuery { Cursor = page.NextCursor }));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Reader().GetAsync(page.Items[0].Id));
        Assert.AreEqual(before, f.Repository.Queries.Count);
        Assert.AreEqual(0, f.Repository.Gets);
    }

    [TestMethod]
    public async Task QueryReplayPurposeSwapTamperingAndExpiryFailBeforeStorage()
    {
        var (f, page) = await Setup();
        var reader = f.Reader();
        await Assert.ThrowsAsync<DiagnosticQueryException>(() => reader.ReadAsync(new DiagnosticQuery { Cursor = page.NextCursor, PageSize = 2 }));
        await Assert.ThrowsAsync<DiagnosticQueryException>(() => reader.ReadAsync(new DiagnosticQuery { Cursor = page.NextCursor, FromUtc = page.Query.FromUtc.AddSeconds(1) }));
        await Assert.ThrowsAsync<DiagnosticQueryException>(() => reader.GetAsync(page.Items[0].Id, new DiagnosticQuery { ToUtc = page.Query.ToUtc.AddSeconds(1) }));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => reader.GetAsync(page.NextCursor!));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => reader.ReadAsync(new DiagnosticQuery { Cursor = page.Items[0].Id }));
        var token = page.NextCursor!;
        var index = token.Length / 2;
        var tampered = token[..index] + (token[index] == 'A' ? "B" : "A") + token[(index + 1)..];
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => reader.ReadAsync(new DiagnosticQuery { Cursor = tampered }));
        f.Time.NowUtc = ErrorReaderFixture.Now.AddMinutes(31);
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => reader.ReadAsync(new DiagnosticQuery { Cursor = token }));
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => reader.GetAsync(page.Items[0].Id));
    }

    [TestMethod]
    public void RecordKindAndPurposeAreAuthenticatedAndStorageStateIsEncrypted()
    {
        var f = new ErrorReaderFixture();
        var codec = new DiagnosticTokenCodec(Options.Create(f.Options));
        var protectedValue = codec.Protect("cursor", "qurvia/Production/system-errors/source", new { State = "ERR|20261015/provider-secret-cursor" });
        Assert.IsFalse(protectedValue.Contains("ERR|", StringComparison.Ordinal));
        Assert.IsFalse(protectedValue.Contains("provider", StringComparison.Ordinal));
        Assert.Throws<DiagnosticTokenException>(() => codec.Unprotect<object>("cursor", "qurvia/Production/system-logs/source", protectedValue));
        Assert.Throws<DiagnosticTokenException>(() => codec.Unprotect<object>("record", "qurvia/Production/system-errors/source", protectedValue));
        foreach(var invalid in new[] { "", "not-a-token", "d1_!!!!", "d1_" + new string('A', 17000) })
            Assert.Throws<DiagnosticTokenException>(() => codec.Unprotect<object>("cursor", "context", invalid));
    }

    [TestMethod]
    public async Task ClockMovementDoesNotChangeTheProtectedQueryOrRenewItsLifetime()
    {
        var (f, page) = await Setup();
        f.Time.NowUtc = ErrorReaderFixture.Now.AddMinutes(20);
        var next = await f.Reader().ReadAsync(new DiagnosticQuery { Cursor = page.NextCursor });
        Assert.AreEqual(page.Query, next.Query);
        f.Time.NowUtc = ErrorReaderFixture.Now.AddMinutes(31);
        await Assert.ThrowsAsync<DiagnosticTokenException>(() => f.Reader().GetAsync(next.Items[0].Id));
    }
}
