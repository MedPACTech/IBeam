using System.Net;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerUserDirectoryTests
{
    [TestMethod]
    public async Task DirectDirectoryCallsWithoutVerifiedContextDoNotReadMembershipOrInvitationStorage()
    {
        var users = new CustomerRows(); var invites = new InvitationRows();
        await using var f = await DelegationFixture.Start(new TenantRows(), users, invites);
        using var scope = f.App.Services.CreateScope();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => scope.ServiceProvider
            .GetRequiredService<IManagementCustomerUserDirectoryService>().ListAsync(users.Tenant, new()));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => scope.ServiceProvider
            .GetRequiredService<IManagementCustomerInvitationDirectoryService>().ListAsync(users.Tenant, new()));
        Assert.AreEqual(0, users.Reads); Assert.AreEqual(0, invites.Reads);
    }
    [TestMethod]
    public async Task MembershipAndInvitationDirectoryUseIndependentSignedOperationsAndSafeMetadata()
    {
        var tenants = new TenantRows(); var users = new CustomerRows(); var invites = new InvitationRows();
        await using var f = await DelegationFixture.Start(tenants, users, invites);
        var path = Path(users.Tenant, "users");
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: path), path: path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var text = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(text);
        Assert.AreEqual("active@example.test", json.RootElement.GetProperty("data")[0].GetProperty("email").GetString());
        Assert.AreEqual("active", json.RootElement.GetProperty("data")[0].GetProperty("status").GetString());
        Assert.DoesNotContain("InviteId", text);
        var invitationPath = Path(users.Tenant, "invites");
        using var wrongOperation = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: invitationPath), path: invitationPath);
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrongOperation.StatusCode); Assert.AreEqual(0, invites.Reads);
        using var invitations = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesRead, path: invitationPath), path: invitationPath);
        Assert.AreEqual(HttpStatusCode.OK, invitations.StatusCode);
        using var directory = JsonDocument.Parse(await invitations.Content.ReadAsStringAsync());
        var items = directory.RootElement.GetProperty("data");
        Assert.AreEqual("pending", items[0].GetProperty("status").GetString());
        Assert.AreEqual("expired", items[1].GetProperty("status").GetString());
        Assert.AreEqual(1, invites.Reads);
        Assert.DoesNotContain("active@example.test", JsonSerializer.Serialize(f.Audit.Rows));
        Assert.DoesNotContain("invite@example.test", JsonSerializer.Serialize(f.Audit.Rows));
    }
    [TestMethod]
    public async Task UserReadWithoutTenantOrInvitationCapabilityCanReadOnlyTheKnownTenantMembership()
    {
        var users = new CustomerRows();
        await using var f = await DelegationFixture.Start(new TenantRows(), users, new InvitationRows());
        f.Credentials.Row = f.Credentials.Row with { Capabilities = [ManagementCustomerIdentityOperations.Info, ManagementCustomerIdentityOperations.UsersRead] };
        var path = Path(users.Tenant, "users/" + users.Rows[0].UserId);
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: path), path: path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var invitePath = Path(users.Tenant, "invites");
        using var denied = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesRead, path: invitePath), path: invitePath);
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
    }
    [TestMethod]
    public async Task SameCustomerInTwoTenantsRevealsOnlyTheRequestedMembership()
    {
        var users = new CustomerRows();
        users.Rows.Add(users.Rows[0] with { TenantId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Email = "other-membership@example.test", RolesCsv = "EnterpriseReader" });
        await using var f = await DelegationFixture.Start(new TenantRows(), users);
        var id = users.Rows[0].UserId;
        var first = Path(users.Tenant, "users/" + id);
        using var one = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: first), path: first);
        Assert.AreEqual(HttpStatusCode.OK, one.StatusCode);
        Assert.DoesNotContain("EnterpriseReader", await one.Content.ReadAsStringAsync());
        var other = Path(Guid.Parse("20000000-0000-0000-0000-000000000002"), "users/" + id);
        using var two = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: other), path: other);
        Assert.AreEqual(HttpStatusCode.OK, two.StatusCode);
        Assert.Contains("EnterpriseReader", await two.Content.ReadAsStringAsync());
        Assert.DoesNotContain("active@example.test", await two.Content.ReadAsStringAsync());
    }
    [TestMethod]
    [DataRow("tenant")] [DataRow("actor")] [DataRow("filter")] [DataRow("type")]
    public async Task MemberCursorsAreTenantActorFilterAndOperationBound(string change)
    {
        var users = new CustomerRows(); var invites = new InvitationRows();
        await using var f = await DelegationFixture.Start(new TenantRows(), users, invites);
        var path = Path(users.Tenant, "users?pageSize=1");
        using var first = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: path), path: path);
        using var page = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var cursor = page.RootElement.GetProperty("continuationToken").GetString()!;
        var tenant = change == "tenant" ? Guid.Parse("20000000-0000-0000-0000-000000000002") : users.Tenant;
        var operation = change == "type" ? ManagementCustomerIdentityOperations.InvitesRead : ManagementCustomerIdentityOperations.UsersRead;
        var next = Path(tenant, (change == "type" ? "invites" : "users") + "?pageSize=1&continuationToken=" + cursor + (change == "filter" ? "&status=disabled" : ""));
        var assertion = await f.Issue(operation, path: next);
        if (change == "actor") assertion = f.Change(assertion, p => p["sub"] = Guid.NewGuid().ToString("D"));
        using var invalid = await f.Send(assertion, path: next);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(1, users.Reads); Assert.AreEqual(0, invites.Reads);
    }
    [TestMethod]
    public async Task MemberFiltersPreserveEmptyContinuationAndDisabledStatus()
    {
        var users = new CustomerRows();
        await using var f = await DelegationFixture.Start(new TenantRows(), users);
        var path = Path(users.Tenant, "users?pageSize=1&status=disabled");
        using var first = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: path), path: path);
        using var page = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.AreEqual(0, page.RootElement.GetProperty("data").GetArrayLength());
        var next = path + "&continuationToken=" + page.RootElement.GetProperty("continuationToken").GetString();
        using var second = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: next), path: next);
        using var data = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.AreEqual("disabled", data.RootElement.GetProperty("data")[0].GetProperty("status").GetString());
    }
    [TestMethod]
    public async Task SmsInvitationRecipientsAndMalformedRoleMetadataAreNotReturned()
    {
        var invites = new InvitationRows();
        invites.Rows[0] = invites.Rows[0] with { DestinationType = "sms", Destination = "PHONE-CANARY" };
        invites.Rows[1] = invites.Rows[1] with { RoleNamesJson = "{\"unexpected\":\"PROFILE-CANARY\"}" };
        await using var f = await DelegationFixture.Start(new TenantRows(), invites: invites);
        var path = Path(invites.Tenant, "invites");
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesRead, path: path), path: path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var text = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(text);
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetArrayLength());
        Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("data")[0].GetProperty("email").ValueKind);
        Assert.DoesNotContain("PHONE-CANARY", text); Assert.DoesNotContain("PROFILE-CANARY", text);
    }
    [TestMethod]
    public async Task ProviderFailuresAndMissingTenantDoNotEchoIdentityPayloads()
    {
        var users = new CustomerRows { Outage = true };
        await using var f = await DelegationFixture.Start(new TenantRows(), users);
        var path = Path(users.Tenant, "users");
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: path), path: path);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("PROVIDER-PAYLOAD", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("PROVIDER-PAYLOAD", JsonSerializer.Serialize(f.Audit.Rows));
        var missing = Path(Guid.NewGuid(), "users");
        using var notFound = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: missing), path: missing);
        Assert.AreEqual(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.AreEqual(1, users.Reads);
    }
    private static string Path(Guid tenant, string resource) => "/api/management-identity/tenants/" + tenant + "/" + resource;
}
internal sealed class CustomerRows : IManagementCustomerUserReadRepository
{
    public Guid Tenant { get; } = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public string SourceId => "fixture-user-source";
    public int Reads { get; private set; }
    public bool Outage { get; set; }
    public List<ManagementCustomerUserReadEntity> Rows { get; } = [
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("30000000-0000-0000-0000-000000000003"),
            "active@example.test", "Active", "Owner", "", DateTimeOffset.UtcNow, null, "v1"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("40000000-0000-0000-0000-000000000004"),
            "disabled@example.test", "Disabled", "Member", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "v2") ];
    public Task<ManagementCustomerReadPage<ManagementCustomerUserReadEntity>> ReadPageAsync(Guid tenantId, int size, string? cursor, CancellationToken ct = default)
    {
        Reads++; ct.ThrowIfCancellationRequested(); if (Outage) throw new Exception("PROVIDER-PAYLOAD");
        var rows = Rows.Where(x => x.TenantId == tenantId).Skip(cursor is null ? 0 : 1).ToArray();
        return Task.FromResult(new ManagementCustomerReadPage<ManagementCustomerUserReadEntity>(rows.Take(size).ToArray(), rows.Length > size ? "provider-user-state" : null));
    }
    public Task<ManagementCustomerUserReadEntity?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        Reads++; return Task.FromResult(Rows.SingleOrDefault(x => x.TenantId == tenantId && x.UserId == userId));
    }
}
internal sealed class InvitationRows : IManagementCustomerInvitationReadRepository
{
    public Guid Tenant { get; } = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public string SourceId => "fixture-invite-source";
    public int Reads { get; private set; }
    public List<ManagementCustomerInvitationReadEntity> Rows { get; } = [
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("50000000-0000-0000-0000-000000000005"),
            "email", "invite@example.test", "Casey", "Example", "pending", "", "[\"Member\"]", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null, null, null, "i1"),
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("60000000-0000-0000-0000-000000000006"),
            "email", "expired@example.test", null, null, "sent", "", "[\"Member\"]", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), null, null, null, "i2") ];
    public Task<ManagementCustomerReadPage<ManagementCustomerInvitationReadEntity>> ReadPageAsync(Guid tenantId, int size, string? cursor, CancellationToken ct = default)
    {
        Reads++; var rows = Rows.Where(x => x.TenantId == tenantId).Skip(cursor is null ? 0 : 1).ToArray();
        return Task.FromResult(new ManagementCustomerReadPage<ManagementCustomerInvitationReadEntity>(rows.Take(size).ToArray(), rows.Length > size ? "provider-invite-state" : null));
    }
    public Task<ManagementCustomerInvitationReadEntity?> FindAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default)
    {
        Reads++; return Task.FromResult(Rows.SingleOrDefault(x => x.TenantId == tenantId && x.InviteId == inviteId));
    }
}
