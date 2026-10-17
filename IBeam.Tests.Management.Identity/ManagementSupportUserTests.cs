using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management;
using IBeam.Management.Identity.Services;
using IBeam.Management.Repositories.AzureTable;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Tests.Management.Identity;

[TestClass]
public sealed class ManagementSupportUserTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task OptionalDirectoryInvitesMfaAndDisableUseOnlyManagementIdentity()
    {
        await using var f = await ManagementIdentityHttpFixture.CreateAsync(supportUsers: true);
        var administrator = await f.LoginAsync();
        const string email = "support-person@example.test";
        using var invite = await Send(f, HttpMethod.Post, "/api/management/support-invitations",
            new { email, organizationId = Guid.NewGuid(), roles = new[] { "ManagementAdministrator", "Owner" } }, administrator.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, invite.StatusCode, await invite.Content.ReadAsStringAsync());
        var secret = f.Sender.Messages.Last(x => x.Destination == email).Code;
        Assert.DoesNotContain(secret, await invite.Content.ReadAsStringAsync());
        using var accepted = await f.Client.PostAsJsonAsync("/api/management/support-invitations/accept",
            new { inviteToken = secret, password = ManagementIdentityHttpFixture.Password, email = "attacker@example.test",
                organizationId = Guid.NewGuid(), roles = new[] { "ManagementAdministrator" } });
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode, await accepted.Content.ReadAsStringAsync());
        var result = await ManagementIdentityHttpFixture.ReadDataAsync<ManagementSupportInvitationAccepted>(accepted);
        Assert.IsTrue(result.RequiresMfaLogin);
        Assert.DoesNotContain("accessToken", await accepted.Content.ReadAsStringAsync());
        await f.WithScopeAsync(async services =>
        {
            var user = (await services.GetRequiredService<IIdentityUserStore>().FindByIdAsync(result.UserId))!;
            Assert.AreEqual(email, user.Email); Assert.IsTrue(user.TwoFactorEnabled); Assert.IsTrue(user.EmailConfirmed);
            var roles = await services.GetRequiredService<ITenantRoleStore>().GetRolesForUserAsync(f.OrganizationId, result.UserId);
            CollectionAssert.AreEqual(new[] { ManagementIdentityRoles.Support }, roles.Select(x => x.Name).ToArray());
        });
        using var replay = await f.Client.PostAsJsonAsync("/api/management/support-invitations/accept",
            new { inviteToken = secret, password = ManagementIdentityHttpFixture.Password });
        Assert.AreEqual(HttpStatusCode.BadRequest, replay.StatusCode);
        var support = await f.LoginAsync(email);
        using var denied = await Send(f, HttpMethod.Get, "/api/management/support-users", null, support.AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using var directory = await Send(f, HttpMethod.Get, "/api/management/support-users", null, administrator.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, directory.StatusCode);
        Assert.Contains(email, await directory.Content.ReadAsStringAsync());
        using var self = await Send(f, HttpMethod.Patch, "/api/management/support-users/" + f.UserId,
            new { isEnabled = false }, administrator.AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, self.StatusCode);
        using var emptyUpdate = await Send(f, HttpMethod.Patch, "/api/management/support-users/" + result.UserId,
            new { }, administrator.AccessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, emptyUpdate.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await f.GetIdentityAsync(support.AccessToken)).StatusCode);
        using var enable = await Send(f, HttpMethod.Patch, "/api/management/support-users/" + result.UserId, new { isEnabled = true }, administrator.AccessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, enable.StatusCode);
        using var disabled = await Send(f, HttpMethod.Patch, "/api/management/support-users/" + result.UserId,
            new { isEnabled = false }, administrator.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, disabled.StatusCode);
        using var stale = await f.GetIdentityAsync(support.AccessToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, stale.StatusCode);
        using var refresh = await f.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = support.RefreshToken });
        Assert.AreEqual(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow("expired")] [DataRow("foreign")] [DataRow("role")] [DataRow("issuer-disabled")] [DataRow("wrong-password")]
    public async Task InvalidInvitationsCannotGrantMembership(string tamper)
    {
        await using var f = await ManagementIdentityHttpFixture.CreateAsync(supportUsers: true);
        var admin = await f.LoginAsync(); const string email = "candidate@example.test";
        using var response = await Send(f, HttpMethod.Post, "/api/management/support-invitations", new { email }, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var dto = await ManagementIdentityHttpFixture.ReadDataAsync<ManagementSupportInvitationDto>(response);
        var token = f.Sender.Messages.Last(x => x.Destination == email).Code;
        Guid? candidate = null;
        await f.WithScopeAsync(async services =>
        {
            var invites = services.GetRequiredService<ITenantInviteStore>();
            var record = (await invites.GetAsync(f.OrganizationId, dto.InviteId))!;
            if (tamper == "expired") await invites.UpdateAsync(record with { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1) });
            if (tamper == "foreign") await invites.UpdateAsync(record with { TenantId = Guid.NewGuid() });
            if (tamper == "role") await invites.UpdateAsync(record with { RoleIds = [Guid.NewGuid()] });
            if (tamper == "issuer-disabled") await services.GetRequiredService<ITenantMembershipStore>().DisableTenantMembershipAsync(f.OrganizationId, f.UserId);
            if (tamper == "wrong-password")
            {
                var created = await services.GetRequiredService<IIdentityUserStore>().CreateAsync(new(email, null, ManagementIdentityHttpFixture.Password));
                candidate = created.User!.UserId;
            }
        });
        using var accepted = await f.Client.PostAsJsonAsync("/api/management/support-invitations/accept",
            new { inviteToken = token, password = tamper == "wrong-password" ? "Wrong-Password!1234" : ManagementIdentityHttpFixture.Password });
        Assert.AreEqual(HttpStatusCode.BadRequest, accepted.StatusCode, await accepted.Content.ReadAsStringAsync());
        await f.WithScopeAsync(async services =>
        {
            var user = await services.GetRequiredService<IIdentityUserStore>().FindByEmailAsync(email);
            if (user is not null) Assert.IsNull(await services.GetRequiredService<ITenantMembershipStore>().GetTenantForUserAsync(user.UserId, f.OrganizationId));
        });
    }

    [TestMethod, TestCategory("StorageIntegration")]
    public async Task RedemptionIsAtomicAcrossProviderInstancesAndContainsNoRawToken()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var table = new TableClient(connection, "Redeem" + Guid.NewGuid().ToString("N"));
        await table.CreateIfNotExistsAsync();
        try
        {
            var org = Guid.NewGuid(); var invite = Guid.NewGuid(); var user = Guid.NewGuid();
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                new AzureTableManagementInvitationRedemptionStore(table).TryClaimAsync(org, invite, user)));
            Assert.AreEqual(1, results.Count(x => x));
        }
        finally { await table.DeleteAsync(); }
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task SupportRoutesStayAbsentWithoutOptInAndAcceptanceModelsAreWriteOnly()
    {
        await using var f = await ManagementIdentityHttpFixture.CreateAsync();
        Assert.AreEqual(HttpStatusCode.NotFound, (await f.Client.GetAsync("/api/management/support-users")).StatusCode);
        var dto = new ManagementSupportInvitationAcceptRequest { InviteToken = "secret-invite", Password = "secret-password" };
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(dto));
        Assert.DoesNotContain("secret", dto.ToString());
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task DeliveryFailuresCannotReturnInvitationTokens()
    {
        await using var f = await ManagementIdentityHttpFixture.CreateAsync(supportUsers: true);
        var admin = await f.LoginAsync();
        f.Sender.FailDelivery = true;
        using var response = await Send(f, HttpMethod.Post, "/api/management/support-invitations",
            new { email = "delivery-failure@example.test" }, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(f.Sender.Messages.Last().Code, await response.Content.ReadAsStringAsync());
        Assert.IsTrue(f.Audit.Transactions.Any(x => !x.Succeeded));
        Assert.DoesNotContain(f.Sender.Messages.Last().Code, JsonSerializer.Serialize(f.Audit.Transactions));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task RoutineAdministrationCannotDisableAnyManagementAdministrator()
    {
        await using var f = await ManagementIdentityHttpFixture.CreateAsync(supportUsers: true);
        var admin = await f.LoginAsync();
        Guid other = Guid.Empty;
        await f.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<IIdentityUserStore>();
            other = (await users.CreateAsync(new("second-admin@example.test", null, ManagementIdentityHttpFixture.Password))).User!.UserId;
            await users.SetTwoFactorAsync(other, true, "email");
            var roles = services.GetRequiredService<ITenantRoleStore>();
            var role = (await roles.GetRolesAsync(f.OrganizationId)).Single(x => x.Name == ManagementIdentityRoles.Administrator);
            await roles.EnsureTenantMembershipAndGrantRolesAsync(new(f.OrganizationId, other, RoleIds: [role.RoleId], UserEmail: "second-admin@example.test"));
        });
        using var patch = await Send(f, HttpMethod.Patch, "/api/management/support-users/" + other, new { isEnabled = false }, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, patch.StatusCode);
        using var legacy = await Send(f, HttpMethod.Post, "/api/management/support-users/" + other + "/disable", null, admin.AccessToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, legacy.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await f.GetIdentityAsync(admin.AccessToken)).StatusCode);
    }
    internal static async Task<HttpResponseMessage> Send(ManagementIdentityHttpFixture f, HttpMethod method, string uri, object? body, string access)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await f.Client.SendAsync(request);
    }
}
