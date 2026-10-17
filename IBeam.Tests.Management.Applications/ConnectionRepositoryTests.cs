using Azure;
using Azure.Data.Tables;
using IBeam.Management;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class ConnectionRepositoryTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task RealStoragePersistsOnlyReferencesAndConditionallyActivatesVerifiedVersions()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite fixture required.");
        var appTable = new TableClient(connection, "AppConn" + Guid.NewGuid().ToString("N"));
        var credentialTable = new TableClient(connection, "AppCred" + Guid.NewGuid().ToString("N"));
        var repository = new AzureTableManagementApplicationCredentialRepository(credentialTable);
        try
        {
            Assert.IsNull(await repository.GetAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
            await Assert.ThrowsAsync<RequestFailedException>(() => credentialTable.AddEntityAsync(new TableEntity("no", "table")));
            await appTable.CreateIfNotExistsAsync(); await credentialTable.CreateIfNotExistsAsync();
            using var fixture = new ApplicationFixture();
            var operations = fixture.Provider.GetRequiredService<IServiceOperationExecutor>();
            var apps = new ManagementApplicationService(new AzureTableManagementApplicationRepository(appTable), fixture, operations);
            var secrets = new MemorySecrets(); var probe = new FakeProbe();
            var service = new ManagementApplicationConnectionService(apps, repository, secrets, probe, fixture, operations,
                Options.Create(new ManagementConnectionOptions()), Options.Create(new ManagementEnvironmentSecretOptions()));
            var app = await apps.CreateAsync(ApplicationFixture.Request());
            var first = await service.PutAsync(app.ApplicationId, new() { ApiKey = ConnectionFixture.Key, ExpectedVersion = app.Version });
            await service.TestAsync(app.ApplicationId);
            var saved = await new AzureTableManagementApplicationRepository(appTable).GetAsync(fixture.OrganizationId, app.ApplicationId);
            var row = (await repository.GetAsync(fixture.OrganizationId, app.ApplicationId, saved!.ActiveCredentialId!.Value))!;
            Assert.AreEqual(app.BaseUrl, row.BaseUrl);
            Assert.AreEqual(2, row.Capabilities.Count);
            Assert.IsNull(await repository.GetAsync(Guid.NewGuid(), app.ApplicationId, row.CredentialId));
            Assert.IsNull(await repository.GetAsync(fixture.OrganizationId, Guid.NewGuid(), row.CredentialId));
            foreach (var table in new[] { appTable, credentialTable })
                await foreach (var raw in table.QueryAsync<TableEntity>())
                    Assert.DoesNotContain(ConnectionFixture.Key, System.Text.Json.JsonSerializer.Serialize(raw));
            probe.Failure = new Exception(ConnectionFixture.Key);
            await Assert.ThrowsAsync<ManagementConnectionUnavailableException>(() => service.PutAsync(app.ApplicationId,
                new() { ApiKey = ConnectionFixture.Key + "pending", ExpectedVersion = first.Version }));
            Assert.AreEqual(saved.ActiveCredentialId, (await new AzureTableManagementApplicationRepository(appTable).GetAsync(fixture.OrganizationId, app.ApplicationId))!.ActiveCredentialId);
        }
        finally { await appTable.DeleteAsync(); await credentialTable.DeleteAsync(); }
    }
}
