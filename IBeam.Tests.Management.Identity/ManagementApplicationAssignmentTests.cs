using System.Net;
using System.Net.Http.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Repositories.AzureTable;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Tests.Management.Identity;

[TestClass]
public sealed class ManagementApplicationAssignmentTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task RealIdentityAndAssignmentRoutesEnforceLiveRolesPermissionsAndRevocation()
    {
        await using var f = await ManagementIdentityHttpFixture.CreateAsync(supportUsers: true, applicationAccess: true);
        var admin = await f.LoginAsync(); const string email = "assigned@example.test";
        using var invite = await ManagementSupportUserTests.Send(f, HttpMethod.Post, "/api/management/support-invitations", new { email }, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, invite.StatusCode);
        var secret = f.Sender.Messages.Last(x => x.Destination == email).Code;
        using var accepted = await f.Client.PostAsJsonAsync("/api/management/support-invitations/accept",
            new { inviteToken = secret, password = ManagementIdentityHttpFixture.Password });
        var support = await ManagementIdentityHttpFixture.ReadDataAsync<ManagementSupportInvitationAccepted>(accepted);
        var reader = await f.LoginAsync(email);
        using var created = await ManagementSupportUserTests.Send(f, HttpMethod.Post, "/api/management/applications",
            new { applicationKey = "qurvia", name = "Qurvia", environment = "Production", baseUrl = "https://api.qurvia.app" }, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var app = await ManagementIdentityHttpFixture.ReadDataAsync<ManagementApplicationDto>(created);
        var route = "/api/management/applications/" + app.ApplicationId + "/support-users";
        using var grant = await ManagementSupportUserTests.Send(f, HttpMethod.Put, route + "/" + support.UserId,
            new { permissions = new[] { ManagementOperations.ErrorsRead } }, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, grant.StatusCode, await grant.Content.ReadAsStringAsync());
        var access = await ManagementIdentityHttpFixture.ReadDataAsync<ManagementApplicationAccessDto>(grant);
        using var denied = await ManagementSupportUserTests.Send(f, HttpMethod.Put, route + "/" + support.UserId,
            new { permissions = new[] { ManagementOperations.LogsRead }, expectedVersion = access.Version }, reader.AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using var list = await ManagementSupportUserTests.Send(f, HttpMethod.Get, route, null, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        Assert.AreEqual("no-store", list.Headers.CacheControl?.ToString());
        using var foreign = await ManagementSupportUserTests.Send(f, HttpMethod.Put, route + "/" + Guid.NewGuid(),
            new { permissions = new[] { ManagementOperations.ErrorsRead } }, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var updated = await ManagementSupportUserTests.Send(f, HttpMethod.Put, route + "/" + support.UserId,
            new { permissions = new[] { ManagementOperations.ErrorsRead }, expectedVersion = access.Version }, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode);
        var latest = await ManagementIdentityHttpFixture.ReadDataAsync<ManagementApplicationAccessDto>(updated);
        using var stale = new HttpRequestMessage(HttpMethod.Delete, route + "/" + support.UserId);
        stale.Headers.Add("Authorization", "Bearer " + admin.AccessToken); stale.Headers.Add("If-Match", access.Version);
        Assert.AreEqual(HttpStatusCode.Conflict, (await f.Client.SendAsync(stale)).StatusCode);
        using var revoke = new HttpRequestMessage(HttpMethod.Delete, route + "/" + support.UserId);
        revoke.Headers.Add("Authorization", "Bearer " + admin.AccessToken); revoke.Headers.Add("If-Match", latest.Version);
        Assert.AreEqual(HttpStatusCode.NoContent, (await f.Client.SendAsync(revoke)).StatusCode);
        using var listed = await ManagementSupportUserTests.Send(f, HttpMethod.Get, route, null, admin.AccessToken);
        Assert.Contains("\"isRevoked\":true", await listed.Content.ReadAsStringAsync());
        // Remove live management admin role: an already-issued JWT loses administration on its next request.
        await f.WithScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<ITenantRoleStore>();
            var current = await roles.GetRolesForUserAsync(f.OrganizationId, f.UserId);
            await roles.RevokeRolesAsync(f.OrganizationId, f.UserId, current.Select(x => x.RoleId).ToArray());
        });
        using var removed = await ManagementSupportUserTests.Send(f, HttpMethod.Get, route, null, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, removed.StatusCode);
    }

    [TestMethod, TestCategory("StorageIntegration")]
    public async Task RealAccessStorageUsesScopedKeysAtomicCreationAndEtags()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var table = new TableClient(connection, "Access" + Guid.NewGuid().ToString("N"));
        var repo = new AzureTableManagementApplicationAccessRepository(table);
        try
        {
            Assert.IsNull(await repo.GetAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
            await Assert.ThrowsAsync<RequestFailedException>(() => table.AddEntityAsync(new TableEntity("no", "table")));
            await table.CreateIfNotExistsAsync();
            var row = new ManagementApplicationAccessEntity { OrganizationId = Guid.NewGuid(), ApplicationId = Guid.NewGuid(),
                UserId = Guid.NewGuid(), Permissions = [ManagementOperations.ErrorsRead], CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow, UpdatedBy = Guid.NewGuid() };
            var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                try { return await repo.CreateAsync(row); } catch (ManagementApplicationConflictException) { return null; }
            }));
            Assert.AreEqual(1, attempts.Count(x => x is not null));
            var saved = attempts.Single(x => x is not null)!;
            Assert.IsNull(await repo.GetAsync(Guid.NewGuid(), row.ApplicationId, row.UserId));
            Assert.IsNull(await repo.GetAsync(row.OrganizationId, Guid.NewGuid(), row.UserId));
            await repo.CreateAsync(row with { UserId = Guid.NewGuid() });
            var page = await repo.ListAsync(row.OrganizationId, row.ApplicationId, null, 1);
            Assert.HasCount(1, page.Items); Assert.IsNotNull(page.Next);
            Assert.AreNotEqual(page.Items[0].UserId, (await repo.ListAsync(row.OrganizationId, row.ApplicationId, page.Next, 1)).Items[0].UserId);
            var current = (await repo.GetAsync(row.OrganizationId, row.ApplicationId, row.UserId))!;
            await repo.ReplaceAsync(current with { RevokedUtc = DateTimeOffset.UtcNow, RevokedBy = row.UpdatedBy }, current.Version);
            await Assert.ThrowsAsync<ManagementApplicationConflictException>(() => repo.ReplaceAsync(current, current.Version));
            Assert.IsNotNull((await repo.GetAsync(row.OrganizationId, row.ApplicationId, row.UserId))!.RevokedUtc);
            await Assert.ThrowsAsync<ManagementApplicationInvalidException>(() => repo.ReplaceAsync(current, "*"));
        }
        finally { await table.DeleteAsync(); }
    }
}
