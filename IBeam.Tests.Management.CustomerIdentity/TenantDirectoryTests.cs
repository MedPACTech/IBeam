using System.Net;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services;
using IBeam.Management.Identity.Target.Services.Tenants;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class TenantDirectoryTests
{
    [TestMethod]
    public void DirectoryProtectionKeysAreNeverSerializedOrDisplayed()
    {
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var options = new ManagementTenantDirectoryOptions { ProtectionKey = key };
        Assert.IsTrue(options.IsValid());
        Assert.DoesNotContain(key, JsonSerializer.Serialize(options));
        Assert.DoesNotContain(key, options.ToString());
    }
    [TestMethod]
    public async Task SignedTenantReadsProjectOnlyFrameworkMetadataAndPreserveOpaqueContinuation()
    {
        var repository = new TenantRows();
        await using var f = await DelegationFixture.Start(repository);
        var path = "/api/management-identity/tenants?pageSize=1";
        using var first = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: path), path: path);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());
        var text = await first.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        Assert.AreEqual(repository.Rows[0].RowKey, json.RootElement.GetProperty("data")[0].GetProperty("tenantId").GetString());
        Assert.DoesNotContain("provider-state", text);
        var cursor = json.RootElement.GetProperty("continuationToken").GetString()!;
        var next = path + "&continuationToken=" + cursor;
        using var second = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: next), path: next);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        using var data = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.AreEqual(repository.Rows[1].RowKey, data.RootElement.GetProperty("data")[0].GetProperty("tenantId").GetString());
        Assert.AreEqual(2, repository.Reads);
        var detail = "/api/management-identity/tenants/" + repository.Rows[0].RowKey;
        using var found = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: detail), path: detail);
        Assert.AreEqual(HttpStatusCode.OK, found.StatusCode);
        Assert.IsTrue(f.Audit.Rows.Any(x => x.Operation == ManagementCustomerIdentityOperations.TenantsRead &&
            x.ActorUserId == f.User && x.SubjectId == Guid.Parse(repository.Rows[0].RowKey)));
        Assert.DoesNotContain("Acme", JsonSerializer.Serialize(f.Audit.Rows));
    }
    [TestMethod]
    public async Task FiltersCanReturnAnEmptyBoundedPageWithContinuation()
    {
        var repository = new TenantRows();
        await using var f = await DelegationFixture.Start(repository);
        var path = "/api/management-identity/tenants?pageSize=1&status=Disabled&namePrefix=beacon";
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: path), path: path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetArrayLength());
        Assert.IsNotNull(json.RootElement.GetProperty("continuationToken").GetString());
        Assert.AreEqual(1, repository.Reads);
    }
    [TestMethod]
    [DataRow("filter")] [DataRow("size")] [DataRow("source")] [DataRow("tamper")]
    public async Task CursorScopeCannotBeChanged(string change)
    {
        var repository = new TenantRows();
        await using var f = await DelegationFixture.Start(repository);
        var path = "/api/management-identity/tenants?pageSize=1";
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: path), path: path);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var cursor = json.RootElement.GetProperty("continuationToken").GetString()!;
        if (change == "source") repository.SourceId = "another-target-source";
        var next = "/api/management-identity/tenants?pageSize=" + (change == "size" ? "2" : "1") +
            "&continuationToken=" + (change == "tamper" ? cursor + "x" : cursor) + (change == "filter" ? "&status=Disabled" : "");
        using var invalid = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: next), path: next);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(1, repository.Reads);
    }
    [TestMethod]
    [DataRow("model")] [DataRow("range")] [DataRow("status")]
    public async Task InvalidQueriesAreGenericAndNeverEchoValues(string kind)
    {
        var repository = new TenantRows();
        await using var f = await DelegationFixture.Start(repository);
        var path = "/api/management-identity/tenants?" + (kind == "model" ? "pageSize=PRIVATE-CANARY" :
            kind == "range" ? "pageSize=101" : "status=PRIVATE-CANARY");
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: path), path: path);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("PRIVATE-CANARY", await response.Content.ReadAsStringAsync());
        Assert.AreEqual(0, repository.Reads);
    }
    [TestMethod]
    public async Task MissingTenantAndProviderFailuresRemainSafeAndUnauthenticatedReadsDoNotReachStorage()
    {
        var repository = new TenantRows();
        await using var f = await DelegationFixture.Start(repository);
        var path = "/api/management-identity/tenants";
        using var missingActor = await f.Send(null, path: path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, missingActor.StatusCode);
        Assert.AreEqual(0, repository.Reads);
        var missing = path + "/" + Guid.NewGuid();
        using var notFound = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: missing), path: missing);
        Assert.AreEqual(HttpStatusCode.NotFound, notFound.StatusCode);
        repository.Outage = true;
        using var unavailable = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: path), path: path);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain("PROVIDER-SECRET", await unavailable.Content.ReadAsStringAsync());
        Assert.DoesNotContain("PROVIDER-SECRET", JsonSerializer.Serialize(f.Audit.Rows));
    }
    [TestMethod]
    public async Task DirectDirectoryServicesRejectMissingAuthorizationBeforeRepositoryAccess()
    {
        var repository = new TenantRows();
        var access = new Mock<IManagementCustomerIdentityTargetAccessService>();
        access.Setup(x => x.RequireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ManagementAccessDeniedException());
        var service = new ManagementTenantDirectoryService(repository, access.Object,
            Options.Create(new ManagementTenantDirectoryOptions { ProtectionKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) }),
            Options.Create(new ManagementCustomerIdentityTargetOptions { DedicatedApplicationRealm = true, IdentityRealmId = Guid.NewGuid() }),
            TimeProvider.System, new ServiceOperationExecutor());
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.ListAsync(new()));
        Assert.AreEqual(0, repository.Reads);
    }
    [TestMethod]
    public async Task ExpiredCursorAndMalformedHistoricalRowsCannotExposeDataOrExtendScanning()
    {
        var repository = new TenantRows();
        var clock = new DirectoryClock();
        var realm = Guid.NewGuid();
        var actor = new ManagementDelegatedActor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), realm, Guid.NewGuid(),
            "atlas", "Testing", ManagementCustomerIdentityOperations.TenantsRead, Guid.NewGuid(), clock.Now.AddHours(1), Guid.NewGuid().ToString("N"));
        var access = new Mock<IManagementCustomerIdentityTargetAccessService>();
        access.Setup(x => x.RequireAsync(ManagementCustomerIdentityOperations.TenantsRead, It.IsAny<CancellationToken>())).ReturnsAsync(actor);
        var service = new ManagementTenantDirectoryService(repository, access.Object,
            Options.Create(new ManagementTenantDirectoryOptions { ProtectionKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), TokenLifetimeMinutes = 1 }),
            Options.Create(new ManagementCustomerIdentityTargetOptions { DedicatedApplicationRealm = true, IdentityRealmId = realm }),
            clock, new ServiceOperationExecutor());
        repository.Rows[0] = repository.Rows[0] with { RowKey = "not-a-tenant", Name = "HISTORICAL-CANARY" };
        var page = await service.ListAsync(new(1));
        Assert.HasCount(0, page.Items); Assert.IsNotNull(page.ContinuationToken);
        clock.Now = clock.Now.AddMinutes(2);
        await Assert.ThrowsAsync<ManagementCustomerIdentityInvalidRequestException>(() => service.ListAsync(new(1, page.ContinuationToken)));
        Assert.AreEqual(1, repository.Reads);
    }
}
internal sealed class DirectoryClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
internal sealed class TenantRows : IManagementCustomerTenantReadRepository
{
    public string SourceId { get; set; } = "fixture-dedicated-source";
    public int Reads { get; private set; }
    public bool Outage { get; set; }
    public List<ManagementCustomerTenantReadEntity> Rows { get; } = [
        new("10000000-0000-0000-0000-000000000001", "Acme Enterprise", "ACME ENTERPRISE", "Active", null, null, "v1"),
        new("20000000-0000-0000-0000-000000000002", "Beacon Enterprise", null, "Disabled", null, null, "v2") ];
    public Task<ManagementCustomerTenantReadPage> ReadPageAsync(int size, string? continuation, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Reads++;
        if (Outage) throw new Exception("PROVIDER-SECRET");
        return Task.FromResult(continuation is null ? new ManagementCustomerTenantReadPage(Rows.Take(size).ToArray(), size < Rows.Count ? "provider-state" : null) :
            new ManagementCustomerTenantReadPage(Rows.Skip(1).Take(size).ToArray(), null));
    }
    public Task<ManagementCustomerTenantReadEntity?> FindAsync(Guid tenantId, CancellationToken ct = default)
    {
        Reads++; return Task.FromResult(Rows.SingleOrDefault(x => x.RowKey == tenantId.ToString("D")));
    }
}
