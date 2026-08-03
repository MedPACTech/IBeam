using IBeam.Identity.Models;
using IBeam.Identity.Repositories.EntityFramework.Data;
using IBeam.Identity.Repositories.EntityFramework.OAuth;
using IBeam.Identity.Repositories.EntityFramework.OAuth.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IBeam.Tests.Identity.Repositories.EntityFramework;

[TestClass]
public sealed class OAuthServerStoreTests
{
    private SqliteConnection _keepAlive = default!;
    private string _connectionString = default!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _connectionString = $"Data Source=OAuth{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync();

        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_keepAlive is not null)
            await _keepAlive.DisposeAsync();
    }

    [TestMethod]
    public async Task ClientStore_RoundTripsEnforcesUniqueIdsAndIsolatesTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            var store = new EntityFrameworkOAuthClientStore(db);
            await store.CreateAsync(CreateClient("client-a", tenantA));
            await store.CreateAsync(CreateClient("client-b", tenantB));
        }

        await using (var db = CreateContext())
        {
            var store = new EntityFrameworkOAuthClientStore(db);
            var loaded = await store.GetAsync("client-a");
            Assert.IsNotNull(loaded);
            CollectionAssert.AreEqual(
                new[] { "https://client-a.example/callback" },
                loaded.RedirectUris.ToArray());
            CollectionAssert.AreEqual(
                new[] { "tool:mcp", "api-scope:work" },
                loaded.AllowedScopes.ToArray());

            var clients = await store.ListByTenantAsync(tenantA);
            Assert.HasCount(1, clients);
            Assert.AreEqual("client-a", clients[0].ClientId);

            var disabledAt = DateTimeOffset.UtcNow;
            var updated = await store.UpdateAsync(loaded with
            {
                Status = OAuthClientStatuses.Disabled,
                UpdatedUtc = disabledAt,
                DisabledUtc = disabledAt
            });
            Assert.IsFalse(updated.IsActive);
        }

        await using (var db = CreateContext())
        {
            var duplicateStore = new EntityFrameworkOAuthClientStore(db);
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() =>
                duplicateStore.CreateAsync(CreateClient("client-a", tenantA)));
        }
    }

    [TestMethod]
    public async Task AuthorizationCodeStore_ConsumesOnceAndRejectsExpiredCodes()
    {
        var now = DateTimeOffset.UtcNow;
        var active = CreateCode("sha256:active", now.AddMinutes(5));
        var expired = CreateCode("sha256:expired", now.AddMinutes(-1));
        await using (var db = CreateContext())
        {
            var store = new EntityFrameworkOAuthAuthorizationCodeStore(db);
            await store.CreateAsync(active);
            await store.CreateAsync(expired);
        }

        await using (var db = CreateContext())
        {
            var store = new EntityFrameworkOAuthAuthorizationCodeStore(db);
            var consumed = await store.TryConsumeAsync(active.CodeHash, now);
            Assert.IsNotNull(consumed);
            Assert.AreEqual(now, consumed.ConsumedUtc);
        }

        await using (var db = CreateContext())
        {
            var store = new EntityFrameworkOAuthAuthorizationCodeStore(db);
            Assert.IsNull(await store.TryConsumeAsync(active.CodeHash, now.AddSeconds(1)));
            Assert.IsNull(await store.TryConsumeAsync(expired.CodeHash, now));
        }
    }

    [TestMethod]
    public async Task ConsentStore_UpsertsByClientResourceAndIsolatesTenant()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var consent = new OAuthConsentRecord(
            Guid.NewGuid(),
            userId,
            tenantId,
            "client-a",
            "https://api.example/mcp",
            ["tool:mcp"],
            now,
            now);

        await using (var db = CreateContext())
        {
            var store = new EntityFrameworkOAuthConsentStore(db);
            await store.UpsertAsync(consent);
            await store.UpsertAsync(consent with
            {
                ConsentId = Guid.NewGuid(),
                Scopes = ["tool:mcp", "api-scope:work"],
                UpdatedUtc = now.AddMinutes(1)
            });
        }

        await using (var db = CreateContext())
        {
            var store = new EntityFrameworkOAuthConsentStore(db);
            var loaded = await store.GetAsync(userId, tenantId, consent.ClientId, consent.Resource);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(consent.ConsentId, loaded.ConsentId);
            Assert.HasCount(2, loaded.Scopes);
            Assert.IsNull(await store.GetAsync(userId, otherTenantId, consent.ClientId, consent.Resource));
            Assert.IsFalse(await store.RevokeAsync(
                userId,
                otherTenantId,
                consent.ClientId,
                consent.Resource,
                now.AddMinutes(2)));
            Assert.IsTrue(await store.RevokeAsync(
                userId,
                tenantId,
                consent.ClientId,
                consent.Resource,
                now.AddMinutes(2)));
        }

        await using (var db = CreateContext())
        {
            var store = new EntityFrameworkOAuthConsentStore(db);
            Assert.IsFalse((await store.GetAsync(userId, tenantId, consent.ClientId, consent.Resource))!.IsActive);
        }
    }

    [TestMethod]
    public void Model_DefinesOAuthTablesAndUniqueConsentLookup()
    {
        using var db = CreateContext();
        Assert.AreEqual(
            "IBeamIdentityOAuthClients",
            db.Model.FindEntityType(typeof(OAuthClientEntity))!.GetTableName());
        Assert.AreEqual(
            "IBeamIdentityOAuthAuthorizationCodes",
            db.Model.FindEntityType(typeof(OAuthAuthorizationCodeEntity))!.GetTableName());

        var consentType = db.Model.FindEntityType(typeof(OAuthConsentEntity));
        Assert.IsNotNull(consentType);
        Assert.AreEqual("IBeamIdentityOAuthConsents", consentType.GetTableName());
        Assert.IsTrue(consentType.GetIndexes().Single(x =>
            x.Properties.Count == 1 && x.Properties[0].Name == nameof(OAuthConsentEntity.LookupKey)).IsUnique);
    }

    private IBeamIdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IBeamIdentityDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new IBeamIdentityDbContext(options);
    }

    private static OAuthClientRecord CreateClient(string clientId, Guid tenantId) =>
        new(
            clientId,
            tenantId,
            clientId,
            OAuthClientTypes.Confidential,
            [$"https://{clientId}.example/callback"],
            [OAuthGrantTypes.AuthorizationCode, OAuthGrantTypes.RefreshToken],
            ["tool:mcp", "api-scope:work"],
            ["https://api.example/mcp"],
            true,
            OAuthClientStatuses.Active,
            "pbkdf2-sha256:v1:test-hash",
            "pbkdf2-sha256",
            DateTimeOffset.UtcNow);

    private static OAuthAuthorizationCodeRecord CreateCode(string hash, DateTimeOffset expiresUtc) =>
        new(
            hash,
            "client-a",
            "https://client-a.example/callback",
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["tool:mcp"],
            "https://api.example/mcp",
            "challenge",
            OAuthCodeChallengeMethods.S256,
            DateTimeOffset.UtcNow,
            expiresUtc);
}
