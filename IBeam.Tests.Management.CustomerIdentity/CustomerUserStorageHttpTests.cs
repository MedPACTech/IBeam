using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Repositories.AzureTable;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerUserStorageHttpTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ReadOnlyExistingMembershipAndInviteTablesExposeNoCredentialPhoneOrProfileFields()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var suffix = Guid.NewGuid().ToString("N");
        var tenantTable = new TableClient(connection, "UserReadTenants" + suffix);
        var userTable = new TableClient(connection, "UserReadMembers" + suffix);
        var inviteTable = new TableClient(connection, "UserReadInvites" + suffix);
        var tenant = Guid.NewGuid(); var otherTenant = Guid.NewGuid(); var user = Guid.NewGuid(); var invite = Guid.NewGuid();
        try
        {
            await tenantTable.CreateIfNotExistsAsync(); await userTable.CreateIfNotExistsAsync(); await inviteTable.CreateIfNotExistsAsync();
            await tenantTable.AddEntityAsync(new TableEntity("TEN", tenant.ToString("D")) { ["Name"] = "Enterprise", ["Status"] = "Active" });
            await userTable.AddEntityAsync(new TableEntity("TEN|" + tenant.ToString("D"), "USR|" + user.ToString("D")) {
                ["TenantId"] = tenant.ToString("D"), ["UserId"] = user.ToString("D"), ["Email"] = "member@example.test", ["Status"] = "Active",
                ["RolesCsv"] = "Member", ["RoleIdsCsv"] = "", ["CreatedAt"] = DateTimeOffset.UtcNow,
                ["PhoneNumber"] = "PHONE-CANARY", ["PasswordHash"] = "PASSWORD-CANARY", ["ProfileJson"] = "PROFILE-CANARY" });
            await userTable.AddEntityAsync(new TableEntity("TEN|" + otherTenant.ToString("D"), "USR|" + user.ToString("D")) {
                ["TenantId"] = otherTenant.ToString("D"), ["UserId"] = user.ToString("D"), ["Email"] = "CROSS-TENANT-CANARY@example.test" });
            await inviteTable.AddEntityAsync(new TableEntity("TEN|" + tenant.ToString("D"), "INV|" + invite.ToString("D")) {
                ["TenantId"] = tenant.ToString("D"), ["InviteId"] = invite.ToString("D"), ["DestinationType"] = "email",
                ["NormalizedDestination"] = "invite@example.test", ["FirstName"] = "Casey", ["Status"] = "sent",
                ["RoleNamesJson"] = "[\"Member\"]", ["RoleIdsCsv"] = "", ["CreatedUtc"] = DateTimeOffset.UtcNow.AddDays(-2),
                ["ExpiresUtc"] = DateTimeOffset.UtcNow.AddDays(-1), ["TokenHash"] = "TOKEN-CANARY", ["ProfileMetadataJson"] = "PROFILE-CANARY",
                ["AccessGrantsJson"] = "GRANTS-CANARY", ["RedirectUrl"] = "https://example.test/?token=REDIRECT-CANARY" });
            await inviteTable.AddEntityAsync(new TableEntity("TOK|TOKEN-CANARY", "INV") { ["TokenHash"] = "TOKEN-CANARY" });
            var before = (await inviteTable.GetEntityAsync<TableEntity>("TEN|" + tenant.ToString("D"), "INV|" + invite.ToString("D"))).Value.ETag;
            TableClient ReadOnly(TableClient table) => new(table.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(10)));
            await using var f = await DelegationFixture.Start(new AzureTableManagementCustomerTenantReadRepository(ReadOnly(tenantTable)),
                new AzureTableManagementCustomerUserReadRepository(ReadOnly(userTable)), new AzureTableManagementCustomerInvitationReadRepository(ReadOnly(inviteTable)));
            var userPath = "/api/management-identity/tenants/" + tenant + "/users";
            using var members = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: userPath), path: userPath);
            Assert.AreEqual(HttpStatusCode.OK, members.StatusCode, await members.Content.ReadAsStringAsync());
            var userText = await members.Content.ReadAsStringAsync();
            Assert.Contains("member@example.test", userText); Assert.DoesNotContain("CANARY", userText);
            var invitePath = "/api/management-identity/tenants/" + tenant + "/invites";
            using var pending = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesRead, path: invitePath), path: invitePath);
            Assert.AreEqual(HttpStatusCode.OK, pending.StatusCode, await pending.Content.ReadAsStringAsync());
            var inviteText = await pending.Content.ReadAsStringAsync();
            Assert.Contains("invite@example.test", inviteText); Assert.Contains("expired", inviteText); Assert.DoesNotContain("CANARY", inviteText);
            using var detail = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesRead, path: invitePath + "/" + invite), path: invitePath + "/" + invite);
            Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
            var after = (await inviteTable.GetEntityAsync<TableEntity>("TEN|" + tenant.ToString("D"), "INV|" + invite.ToString("D"))).Value;
            Assert.AreEqual(before, after.ETag); Assert.AreEqual("sent", after.GetString("Status"));
            Assert.DoesNotContain("CANARY", JsonSerializer.Serialize(f.Audit.Rows));
            Assert.DoesNotContain("member@example.test", JsonSerializer.Serialize(f.Audit.Rows));
        }
        finally { await tenantTable.DeleteAsync(); await userTable.DeleteAsync(); await inviteTable.DeleteAsync(); }
    }
}
