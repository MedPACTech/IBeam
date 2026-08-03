using Azure;
using Azure.Data.Tables;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Schema;
using IBeam.Identity.Repositories.AzureTable.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Repositories.AzureTable;

[TestClass]
public sealed class OAuthServerStoreTests
{
    private const string ConnectionString = "UseDevelopmentStorage=true";

    private TableServiceClient _service = default!;
    private AzureTableIdentityOptions _options = default!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await EnsureAzuriteAvailableAsync();
        _service = new TableServiceClient(ConnectionString);
        _options = new AzureTableIdentityOptions
        {
            StorageConnectionString = ConnectionString,
            TablePrefix = $"Oa{Guid.NewGuid():N}"[..12]
        };
        _options.Validate();
        await CreateOAuthTablesAsync();
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_service is null || _options is null)
            return;

        await foreach (var table in _service.QueryAsync(filter: $"TableName ge '{_options.TablePrefix}'"))
        {
            if (table.Name.StartsWith(_options.TablePrefix, StringComparison.Ordinal))
                await _service.DeleteTableAsync(table.Name);
        }
    }

    [TestMethod]
    public async Task ClientStore_RoundTripsUpdatesAndIsolatesTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var store = new AzureTableOAuthClientStore(_service, Options.Create(_options));

        var created = await store.CreateAsync(CreateClient("client-a", tenantA));
        await store.CreateAsync(CreateClient("client-b", tenantB));

        var loaded = await store.GetAsync(created.ClientId);
        Assert.IsNotNull(loaded);
        CollectionAssert.AreEqual(created.RedirectUris.ToArray(), loaded.RedirectUris.ToArray());
        CollectionAssert.AreEqual(created.AllowedScopes.ToArray(), loaded.AllowedScopes.ToArray());
        Assert.AreEqual("pbkdf2-sha256", loaded.ClientSecretHashAlgorithm);

        var tenantAClients = await store.ListByTenantAsync(tenantA);
        Assert.HasCount(1, tenantAClients);
        Assert.AreEqual("client-a", tenantAClients[0].ClientId);

        var disabledAt = DateTimeOffset.UtcNow;
        var updated = await store.UpdateAsync(loaded with
        {
            Status = OAuthClientStatuses.Disabled,
            UpdatedUtc = disabledAt,
            DisabledUtc = disabledAt
        });
        Assert.IsFalse(updated.IsActive);
        Assert.IsNotNull((await store.GetAsync("client-a"))!.DisabledUtc);
    }

    [TestMethod]
    public async Task AuthorizationCodeStore_ConsumesExactlyOnceAndRejectsExpiredCodes()
    {
        var store = new AzureTableOAuthAuthorizationCodeStore(_service, Options.Create(_options));
        var now = DateTimeOffset.UtcNow;
        var active = CreateCode("sha256:active-code", now.AddMinutes(5));
        await store.CreateAsync(active);

        var attempts = await Task.WhenAll(
            store.TryConsumeAsync(active.CodeHash, now),
            store.TryConsumeAsync(active.CodeHash, now));

        Assert.AreEqual(1, attempts.Count(x => x is not null));
        Assert.IsNotNull((await store.GetByHashAsync(active.CodeHash))!.ConsumedUtc);
        Assert.IsNull(await store.TryConsumeAsync(active.CodeHash, now.AddSeconds(1)));

        var expired = CreateCode("sha256:expired-code", now.AddMinutes(-1));
        await store.CreateAsync(expired);
        Assert.IsNull(await store.TryConsumeAsync(expired.CodeHash, now));
    }

    [TestMethod]
    public async Task ConsentStore_IsTenantScopedAndRevokesConsent()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var store = new AzureTableOAuthConsentStore(_service, Options.Create(_options));
        var consent = new OAuthConsentRecord(
            Guid.NewGuid(),
            userId,
            tenantId,
            "client-a",
            "https://api.example/mcp",
            ["tool:mcp", "api-scope:work"],
            now,
            now);

        await store.UpsertAsync(consent);

        var loaded = await store.GetAsync(userId, tenantId, consent.ClientId, consent.Resource);
        Assert.IsNotNull(loaded);
        Assert.IsTrue(loaded.IsActive);
        Assert.IsNull(await store.GetAsync(userId, otherTenantId, consent.ClientId, consent.Resource));
        Assert.IsFalse(await store.RevokeAsync(
            userId,
            otherTenantId,
            consent.ClientId,
            consent.Resource,
            now.AddMinutes(1)));

        Assert.IsTrue(await store.RevokeAsync(
            userId,
            tenantId,
            consent.ClientId,
            consent.Resource,
            now.AddMinutes(1)));
        Assert.IsFalse((await store.GetAsync(userId, tenantId, consent.ClientId, consent.Resource))!.IsActive);
    }

    [TestMethod]
    public async Task SchemaManager_ProvisionsOAuthTablesAndVersionTwo()
    {
        await DeleteOAuthTablesAsync();
        var identityConfiguration = new IdentityConfiguration
        {
            TablePrefix = _options.TablePrefix,
            IndexTableName = _options.IndexTableName,
            UserTableName = _options.UserTableName,
            RoleTableName = _options.RoleTableName
        };
        var manager = new AzureTableIdentitySchemaManager(
            _service,
            identityConfiguration,
            Options.Create(_options),
            NullLogger<AzureTableIdentitySchemaManager>.Instance);

        var pending = await manager.GetStatusAsync();
        Assert.IsTrue(pending.PendingSteps.Any(x =>
            x.Version == 2 &&
            x.Description.Contains(_options.OAuthClientsTableName, StringComparison.Ordinal)));

        await manager.ApplyAsync();

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var table in _service.QueryAsync())
            tables.Add(table.Name);
        Assert.Contains(_options.FullTableName(_options.OAuthClientsTableName), tables);
        Assert.Contains(_options.FullTableName(_options.OAuthAuthorizationCodesTableName), tables);
        Assert.Contains(_options.FullTableName(_options.OAuthConsentsTableName), tables);

        var status = await manager.GetStatusAsync();
        Assert.AreEqual(2, status.CurrentVersion);
        Assert.AreEqual(2, status.TargetVersion);
        Assert.IsTrue(status.IsUpToDate);
    }

    [TestMethod]
    public void OAuthKeys_AreDeterministicAndAzureSafe()
    {
        var clientId = "https://client.example/oauth/metadata?id=1";
        var resource = "https://api.example/api/mcp";

        var first = _options.OAuthClientsRk(clientId);
        var second = _options.OAuthClientsRk(clientId);

        Assert.AreEqual(first, second);
        Assert.IsFalse(first.Any(c => c is '/' or '\\' or '#' or '?'));
        Assert.IsFalse(_options.OAuthConsentsRk(clientId, resource).Any(c => c is '/' or '\\' or '#' or '?'));
    }

    private async Task CreateOAuthTablesAsync()
    {
        await _service.CreateTableIfNotExistsAsync(_options.FullTableName(_options.OAuthClientsTableName));
        await _service.CreateTableIfNotExistsAsync(_options.FullTableName(_options.OAuthAuthorizationCodesTableName));
        await _service.CreateTableIfNotExistsAsync(_options.FullTableName(_options.OAuthConsentsTableName));
    }

    private async Task DeleteOAuthTablesAsync()
    {
        await DeleteIfExistsAsync(_options.FullTableName(_options.OAuthClientsTableName));
        await DeleteIfExistsAsync(_options.FullTableName(_options.OAuthAuthorizationCodesTableName));
        await DeleteIfExistsAsync(_options.FullTableName(_options.OAuthConsentsTableName));
    }

    private async Task DeleteIfExistsAsync(string tableName)
    {
        try
        {
            await _service.DeleteTableAsync(tableName);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }

    private static OAuthClientRecord CreateClient(string clientId, Guid tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        return new OAuthClientRecord(
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
            now);
    }

    private static OAuthAuthorizationCodeRecord CreateCode(string hash, DateTimeOffset expiresUtc)
    {
        var now = DateTimeOffset.UtcNow;
        return new OAuthAuthorizationCodeRecord(
            hash,
            "client-a",
            "https://client-a.example/callback",
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["tool:mcp"],
            "https://api.example/mcp",
            "challenge",
            OAuthCodeChallengeMethods.S256,
            now,
            expiresUtc);
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
