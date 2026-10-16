using Azure;
using Azure.Data.Tables;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Repositories.AzureTable;

namespace IBeam.Tests.Management.Applications;
[TestClass]
public sealed class ApplicationRepositoryTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task AtomicUniquenessOrganizationPagingAndEtagsWorkWithRealProvider()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite fixture required.");
        var table = new TableClient(connection, "Apps" + Guid.NewGuid().ToString("N"));
        var repo = new AzureTableManagementApplicationRepository(table);
        try
        {
            Assert.IsNull(await repo.GetAsync(Guid.NewGuid(), Guid.NewGuid()));
            await Assert.ThrowsAsync<RequestFailedException>(() => table.AddEntityAsync(new TableEntity("no", "table")));
            await table.CreateIfNotExistsAsync();
            var org = Guid.NewGuid();
            var row = new ManagementApplicationEntity { OrganizationId = org, ApplicationId = Guid.NewGuid(),
                ApplicationKey = "qurvia", Environment = "Production", Name = "Qurvia", BaseUrl = "https://api.qurvia.app",
                CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow, CreatedBy = "operator", UpdatedBy = "operator" };
            var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { return await repo.CreateAsync(row with { ApplicationId = Guid.NewGuid() }); }
                catch (ManagementApplicationConflictException) { return null; }
            }));
            Assert.HasCount(1, attempts.Where(x => x is not null));
            var saved = attempts.Single(x => x is not null)!;
            Assert.IsFalse(string.IsNullOrWhiteSpace(saved.Version));
            await Assert.ThrowsAsync<ManagementApplicationConflictException>(() => repo.CreateAsync(row with { Environment = "production" }));
            await repo.CreateAsync(row with { OrganizationId = Guid.NewGuid() });
            Assert.IsNull(await repo.GetAsync(Guid.NewGuid(), saved.ApplicationId));
            await repo.CreateAsync(row with { ApplicationId = Guid.NewGuid(), Environment = "Test" });
            var first = await repo.ListAsync(org, null, 1);
            Assert.HasCount(1, first.Items); Assert.IsNotNull(first.Next);
            var second = await repo.ListAsync(org, first.Next, 1);
            Assert.HasCount(1, second.Items);
            Assert.AreNotEqual(first.Items[0].ApplicationId, second.Items[0].ApplicationId);
            var current = (await repo.GetAsync(org, saved.ApplicationId))!;
            var changed = await repo.ReplaceAsync(current with { IsEnabled = false }, current.Version);
            await Assert.ThrowsAsync<ManagementApplicationConflictException>(() => repo.ReplaceAsync(current, current.Version));
            Assert.IsFalse((await repo.GetAsync(org, saved.ApplicationId))!.IsEnabled);
            await Assert.ThrowsAsync<ManagementApplicationInvalidException>(() => repo.ReplaceAsync(changed, "*"));
        }
        finally { await table.DeleteAsync(); }
    }
}
