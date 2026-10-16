using Azure;
using Azure.Data.Tables;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Repositories.AzureTable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Credentials;

[TestClass]
public sealed class ManagementCredentialRepositoryTests
{
    [TestMethod]
    [TestCategory("StorageIntegration")]
    public async Task RealProviderUsesPointBindingsEtagsAndNeverCreatesRuntimeTables()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Set IBEAM_MANAGEMENT_TEST_STORAGE to a private Azurite fixture.");
        var table = new TableClient(connection, "Cred" + Guid.NewGuid().ToString("N"));
        var repository = new AzureTableManagementApiCredentialRepository(table);
        try
        {
            Assert.IsNull(await repository.GetAsync("qurvia", "Production", Guid.NewGuid()));
            await Assert.ThrowsAsync<RequestFailedException>(() => table.AddEntityAsync(new TableEntity("fixture", "missing-table")));
            await table.CreateIfNotExistsAsync();
            var row = new ManagementApiCredentialEntity
            {
                CredentialId = Guid.NewGuid(), ApplicationKey = "qurvia", Environment = "Production",
                SecretHash = "test-hash-only", Capabilities = [ManagementOperations.ErrorsRead],
                CreatedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1),
                CreatedByOperatorId = Guid.NewGuid()
            };
            var created = await repository.CreateAsync(row);
            Assert.IsFalse(string.IsNullOrWhiteSpace(created.Version));
            Assert.IsNull(await repository.GetAsync("bindry", "Production", row.CredentialId));
            Assert.IsNull(await repository.GetAsync("qurvia", "Test", row.CredentialId));
            var read = (await repository.GetAsync("qurvia", "Production", row.CredentialId))!;
            CollectionAssert.AreEqual(row.Capabilities.ToArray(), read.Capabilities.ToArray());
            Assert.AreEqual(row.CreatedByOperatorId, read.CreatedByOperatorId);
            var revoked = await repository.ReplaceAsync(read with { Status = "Revoked", RevokedUtc = DateTimeOffset.UtcNow }, read.Version);
            Assert.AreNotEqual(read.Version, revoked.Version);
            await Assert.ThrowsAsync<ManagementCredentialConflictException>(() => repository.ReplaceAsync(read with { SecretHash = "stale-rotation" }, read.Version));
            var final = (await repository.GetAsync("qurvia", "Production", row.CredentialId))!;
            Assert.AreEqual("Revoked", final.Status);
            await Assert.ThrowsAsync<ArgumentException>(() => repository.ReplaceAsync(final, "*"));
            await using var lifecycle = new CredentialFixture(customize: services =>
                services.AddSingleton<IManagementApiCredentialRepository>(repository));
            var issued = await lifecycle.IssueAsync(ManagementOperations.DiagnosticsInfo, ManagementOperations.LogsRead);
            Assert.IsNotNull(await lifecycle.Validator.ValidateAsync(issued.ApiKey));
            var rotated = await lifecycle.Provisioning.RotateAsync(issued.Credential.CredentialId);
            Assert.IsNull(await lifecycle.Validator.ValidateAsync(issued.ApiKey));
            Assert.IsNotNull(await lifecycle.Validator.ValidateAsync(rotated.ApiKey));
            await lifecycle.Provisioning.RevokeAsync(issued.Credential.CredentialId);
            Assert.IsNull(await lifecycle.Validator.ValidateAsync(rotated.ApiKey));
        }
        finally
        {
            await table.DeleteAsync();
        }
    }

    [TestMethod]
    public void StorageRegistrationIsIndependentAndDoesNotFallbackToIdentity()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["IBeam:Identity:AzureTable:StorageConnectionString"] = "customer-only" }).Build();
        var services = new ServiceCollection();
        services.AddIBeamManagementAzureTableCredentialStore(config);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IManagementApiCredentialRepository>());
        Assert.IsFalse(typeof(AzureTableManagementApiCredentialRepository).Assembly.GetReferencedAssemblies()
            .Any(x => x.Name!.Contains("Services", StringComparison.Ordinal) || x.Name.Contains("Identity", StringComparison.Ordinal)));
    }
    [TestMethod]
    [TestCategory("StorageIntegration")]
    public async Task RealAzureCredentialAuthenticatesHttpAndIsRejectedAfterRevocation()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Set IBEAM_MANAGEMENT_TEST_STORAGE to private Azurite.");
        var table = new TableClient(connection, "HttpCred" + Guid.NewGuid().ToString("N"));
        await table.CreateIfNotExistsAsync();
        try
        {
            var repository = new AzureTableManagementApiCredentialRepository(table);
            await using var fixture = await CredentialApiFixture.CreateAsync(repository: repository);
            var issued = await fixture.Credentials.IssueAsync();
            using var info = await fixture.GetInfoAsync(issued.ApiKey);
            Assert.AreEqual(System.Net.HttpStatusCode.OK, info.StatusCode);
            await fixture.Credentials.Provisioning.RevokeAsync(issued.Credential.CredentialId);
            using var denied = await fixture.GetInfoAsync(issued.ApiKey);
            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        finally { await table.DeleteAsync(); }
    }
}
