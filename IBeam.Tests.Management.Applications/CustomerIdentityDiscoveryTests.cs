using System.Net;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.Entities;
using IBeam.Management.Repositories.AzureTable;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class CustomerIdentityDiscoveryTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task TypedCustomerMetadataRoundTripsWhileLegacyRowsRemainReadable()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite fixture required.");
        var table = new TableClient(connection, "CustMeta" + Guid.NewGuid().ToString("N"));
        try
        {
            await table.CreateIfNotExistsAsync();
            var repository = new AzureTableManagementApplicationCredentialRepository(table);
            var legacy = new ManagementApplicationCredentialEntity { OrganizationId = Guid.NewGuid(), ApplicationId = Guid.NewGuid(),
                CredentialId = Guid.NewGuid(), BaseUrl = "https://api.qurvia.app", SecretReference = "fixture-reference",
                VerifiedUtc = DateTimeOffset.UtcNow, CreatedBy = "operator", Capabilities = [ManagementOperations.DiagnosticsInfo] };
            await repository.CreateAsync(legacy);
            Assert.IsNull((await repository.GetAsync(legacy.OrganizationId, legacy.ApplicationId, legacy.CredentialId))!.CustomerIdentity);
            var info = new ManagementCustomerIdentityInfo("qurvia", "Production", Guid.NewGuid(), "dedicated-app", 1,
                [ManagementCustomerIdentityOperations.Info]);
            var current = legacy with { CredentialId = Guid.NewGuid(), CustomerIdentity = new("available", info),
                Capabilities = [ManagementCustomerIdentityOperations.Info] };
            await repository.CreateAsync(current);
            var saved = await repository.GetAsync(current.OrganizationId, current.ApplicationId, current.CredentialId);
            Assert.AreEqual(info.IdentityRealmId, saved!.CustomerIdentity!.Info!.IdentityRealmId);
            Assert.AreEqual("available", saved.CustomerIdentity.Status);
            Assert.HasCount(1, saved.Capabilities);
            Assert.IsNull(await repository.GetAsync(Guid.NewGuid(), current.ApplicationId, current.CredentialId));
        }
        finally { await table.DeleteAsync(); }
    }
    [TestMethod]
    [DataRow("identity-only")] [DataRow("combined")]
    public async Task TypedProfileActivationPersistsCustomerRealmWithoutChangingOldConstructors(string mode)
    {
        using var f = new ConnectionFixture();
        var app = await f.CreateAsync();
        var customer = new ManagementCustomerIdentityInfo("qurvia", "Production", Guid.NewGuid(), "dedicated-app", 1,
            [ManagementCustomerIdentityOperations.Info]);
        var profile = new StaticProfile(new(mode == "combined" ? f.Probe.Info : null, new("available", customer)));
        var service = new ManagementApplicationConnectionService(f.Apps.Provider.GetRequiredService<ManagementApplicationService>(),
            f.Credentials, f.Secrets, f.Probe, f.Apps, f.Apps.Provider.GetRequiredService<IServiceOperationExecutor>(),
            Options.Create(new ManagementConnectionOptions { TimeoutSeconds = 2 }), Options.Create(f.Bindings), profile);
        var result = await service.PutAsync(app.ApplicationId, new() { ApiKey = ConnectionFixture.Key, ExpectedVersion = app.Version });
        Assert.AreEqual(customer.IdentityRealmId, result.CustomerIdentity!.Info!.IdentityRealmId);
        Assert.AreEqual(customer.IdentityRealmId, f.Credentials.Rows.Single().CustomerIdentity!.Info!.IdentityRealmId);
        Assert.AreEqual(mode == "combined", result.Capabilities.Contains(ManagementOperations.ErrorsRead));
        Assert.AreEqual(0, f.Probe.Calls);
        var current = f.Apps.Repository.Rows[(f.Apps.OrganizationId, app.ApplicationId)].ActiveCredentialId;
        profile.Value = profile.Value with { CustomerIdentity = new("available", customer with { ApplicationKey = "other" }) };
        await Assert.ThrowsAsync<ManagementConnectionInvalidException>(() => service.PutAsync(app.ApplicationId,
            new() { ApiKey = ConnectionFixture.Key, ExpectedVersion = result.Version }));
        Assert.AreEqual(current, f.Apps.Repository.Rows[(f.Apps.OrganizationId, app.ApplicationId)].ActiveCredentialId);
    }
    [TestMethod]
    public async Task DiscoveryRoutesRequireOptInAndLiveAppAssignmentEvenForAdministrators()
    {
        await using var off = await PortalHttpFixture.CreateAsync(install: false);
        using var missing = await off.Get("/api/management/applications/" + Guid.NewGuid() + "/customer-management");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerIdentity: true);
        var id = await f.Data.AddApp();
        var path = "/api/management/applications/" + id + "/customer-management";
        foreach (var token in new[] { "other", "foreign", "machine", "admin" })
        {
            using var denied = await f.Get(path, token);
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var customer = await f.Get(path, "customer");
        Assert.AreEqual(HttpStatusCode.Unauthorized, customer.StatusCode);
        using var first = await f.Get(path);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());
        Assert.AreEqual("no-store", first.Headers.CacheControl!.ToString());
        Assert.DoesNotContain(ConnectionFixture.Key, await first.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.AreEqual("not-installed", json.RootElement.GetProperty("data").GetProperty("status").GetString());
        var grant = f.Data.Access.Access.Rows[(f.Data.Access.Org, id, f.Data.Access.Reader)];
        f.Data.Access.Access.Rows[(f.Data.Access.Org, id, f.Data.Access.Reader)] = grant with { RevokedUtc = DateTimeOffset.UtcNow };
        using var revoked = await f.Get(path);
        Assert.AreEqual(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.AreEqual(0, f.Data.Secrets.Reads);
        Assert.AreEqual(0, f.Data.Backend.Calls);
    }
    [TestMethod]
    [DataRow("available")] [DataRow("incompatible")] [DataRow("unavailable")]
    public async Task AuthorizedDiscoveryReturnsOnlyValidatedConnectionMetadata(string status)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerIdentity: true, audit: true);
        var id = await f.Data.AddApp();
        var row = f.Data.Credentials.Rows.Single();
        var info = new ManagementCustomerIdentityInfo("qurvia", "Production", Guid.NewGuid(), "dedicated-app", 1,
            [ManagementCustomerIdentityOperations.Info]);
        f.Data.Credentials.Rows[0] = row with { CustomerIdentity = new(status, status == "available" ? info : null) };
        using var response = await f.Get("/api/management/applications/" + id + "/customer-management");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SecretReference", body);
        Assert.DoesNotContain(ConnectionFixture.Key, body);
        using var json = JsonDocument.Parse(body);
        Assert.AreEqual(status, json.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.IsTrue(f.Audit.Rows.Any(x => x.Operation == "management.portal.customer-identity.info" && x.ApplicationId == id));
        Assert.DoesNotContain("identityRealmId", JsonSerializer.Serialize(f.Audit.Rows));
    }
    private sealed class StaticProfile(ManagementTargetProfile value) : IManagementTargetProfileProbe
    {
        public ManagementTargetProfile Value { get; set; } = value;
        public Task<ManagementTargetProfile> ProbeProfileAsync(string origin, ManagementSecretValue key, CancellationToken ct = default) => Task.FromResult(Value);
    }
}
