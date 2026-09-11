using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Repositories.AzureTable;

/// <summary>
/// BIND-0084's "remember this device" only matters if a remembered session's flag survives being
/// saved and reloaded from the real store - JwtTokenService.RefreshAccessTokenAsync reloads the
/// session on every refresh, so any gap here silently downgrades every remembered session to a
/// normal one on its very first refresh. JwtTokenServiceTests only exercises this against a mocked
/// IAuthSessionStore, which can't catch a mapping gap in the real AzureTableAuthSessionStore/
/// AuthSessionEntity - hence a real round trip against Azurite here instead.
/// </summary>
[TestClass]
public sealed class AuthSessionStoreTests
{
    private const string ConnectionString = "UseDevelopmentStorage=true";

    [TestInitialize]
    public async Task InitializeAsync() => await EnsureAzuriteAvailableAsync();

    [TestMethod]
    public async Task SaveAsync_ThenGetByRefreshTokenHash_RoundTripsRememberedTrue()
    {
        var store = CreateStore();
        var record = NewRecord(remembered: true);

        await store.SaveAsync(record);
        var reloaded = await store.GetByRefreshTokenHashAsync(record.RefreshTokenHash);

        Assert.IsNotNull(reloaded);
        Assert.IsTrue(reloaded!.Remembered);
    }

    [TestMethod]
    public async Task SaveAsync_ThenGetByRefreshTokenHash_RoundTripsRememberedFalse()
    {
        var store = CreateStore();
        var record = NewRecord(remembered: false);

        await store.SaveAsync(record);
        var reloaded = await store.GetByRefreshTokenHashAsync(record.RefreshTokenHash);

        Assert.IsNotNull(reloaded);
        Assert.IsFalse(reloaded!.Remembered);
    }

    private static AzureTableAuthSessionStore CreateStore() => new(
        new TableServiceClient(ConnectionString),
        Options.Create(new AzureTableIdentityOptions { StorageConnectionString = ConnectionString }));

    private static AuthSessionRecord NewRecord(bool remembered)
    {
        var now = DateTimeOffset.UtcNow;
        return new AuthSessionRecord(
            RefreshTokenHash: Guid.NewGuid().ToString("N"),
            SessionId: Guid.NewGuid().ToString(),
            UserId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            ClaimsJson: "[]",
            CreatedAt: now,
            LastSeenAt: now,
            RefreshTokenExpiresAt: now.AddDays(remembered ? 60 : 7),
            Remembered: remembered);
    }

    private static async Task EnsureAzuriteAvailableAsync()
    {
        try
        {
            var service = new TableServiceClient(ConnectionString);
            var probe = service.GetTableClient($"probe{Guid.NewGuid():N}"[..20]);
            await probe.CreateIfNotExistsAsync();
            await probe.DeleteAsync();
        }
        catch (Exception ex) when (
            ex is RequestFailedException or
            InvalidOperationException or
            HttpRequestException or
            AggregateException)
        {
            Assert.Inconclusive("Azurite is not reachable. Start Azurite and re-run tests.");
        }
    }
}
