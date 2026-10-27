using System.Text.Json;
using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Identity.Services.Invites;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Management.Identity.Target.Services.Provisioning;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerProvisioningStorageTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task NativeApprovedMemberInvitationAndPrivateJournalRecoverWithReadOnlyCatalogAndExactMetadataVersions()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var suffix = Guid.NewGuid().ToString("N");
        var roles = new TableClient(connection, "ProvisionRoles" + suffix); var invitations = new TableClient(connection, "ProvisionInvites" + suffix); var metadata = new TableClient(connection, "ProvisionJournal" + suffix);
        try {
            await roles.CreateIfNotExistsAsync(); await invitations.CreateIfNotExistsAsync(); await metadata.CreateIfNotExistsAsync();
            var f = new ProvisioningFixture();
            var roleEntity = new TableEntity("TEN|" + f.Tenant.ToString("D"), "ROL|" + f.Role.ToString("D")) { ["TenantId"] = f.Tenant.ToString("D"),
                ["RoleId"] = f.Role.ToString("D"), ["Name"] = "Member", ["NormalizedName"] = "MEMBER", ["Status"] = "Active", ["CreatedAt"] = DateTimeOffset.UtcNow,
                ["IsSystem"] = false, ["PermissionsJson"] = "PRIVATE-PERMISSION-PROFILE-CANARY", ["Description"] = "PRIVATE-PERMISSION-PROFILE-CANARY" };
            await roles.AddEntityAsync(roleEntity);
            var protectedRole = Guid.NewGuid(); await roles.AddEntityAsync(new TableEntity(roleEntity.PartitionKey, "ROL|" + protectedRole.ToString("D")) {
                ["TenantId"] = f.Tenant.ToString("D"), ["RoleId"] = protectedRole.ToString("D"), ["Name"] = "SupportAdmin", ["Status"] = "Active" });
            var before = (await roles.GetEntityAsync<TableEntity>(roleEntity.PartitionKey, roleEntity.RowKey)).Value.ETag;
            var sas = roles.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(15));
            var roleReads = new AzureTableManagementAssignableRoleReadRepository(new TableClient(sas));
            var read = await roleReads.ReadAsync(f.Tenant, ["Member"]); Assert.HasCount(1, read); Assert.AreEqual(f.Role, read[0].RoleId);
            Assert.DoesNotContain("PRIVATE-PERMISSION", JsonSerializer.Serialize(read));
            var nativeOptions = Options.Create(new AzureTableIdentityOptions { TenantRolesTableName = roles.Name, TenantInvitesTableName = invitations.Name, CreateTablesIfNotExists = false });
            var tables = new TableServiceClient(connection); var roleStore = new AzureTableTenantRoleStore(tables, nativeOptions); var inviteStore = new AzureTableTenantInviteStore(tables, nativeOptions);
            var tenantService = new Mock<IIdentityTenantService>(MockBehavior.Strict);
            tenantService.Setup(x => x.FindByIdAsync(f.Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new IdentityTenant(f.Tenant, "Private"));
            var sent = new List<IdentitySenderMessage>(); var sender = new Mock<IIdentityCommunicationSender>();
            sender.Setup(x => x.SendAsync(It.IsAny<IdentitySenderMessage>(), It.IsAny<CancellationToken>())).Callback((IdentitySenderMessage value, CancellationToken _) => sent.Add(value)).Returns(Task.CompletedTask);
            var external = new TenantInviteService(roleStore, inviteStore, tenantService.Object, f.Data.Users.Object,
                Mock.Of<ITenantRoleService>(MockBehavior.Strict), Mock.Of<ITenantProvisioningService>(MockBehavior.Strict), f.Data.Memberships.Object,
                Mock.Of<IIdentityUserExtensionCoordinator>(MockBehavior.Strict), sender.Object,
                new DefaultTenantInviteUrlBuilder(new DefaultTenantInviteLinkPolicy(Options.Create(new TenantInviteLinkOptions {
                    AllowedOrigins = { "https://customer.example" }, DefaultAcceptUrl = "https://customer.example/invites/accept" }), NullLogger<DefaultTenantInviteLinkPolicy>.Instance)),
                new DefaultTenantInviteMessageFactory(), Mock.Of<IOtpService>(MockBehavior.Strict), Mock.Of<IOtpChallengeStore>(MockBehavior.Strict), Mock.Of<ITokenService>(MockBehavior.Strict),
                Mock.Of<IAuthEventPublisher>(), Options.Create(new AuthEventOptions()), NullLogger<TenantInviteService>.Instance);
            var journal = new AzureTableManagementCustomerProvisioningRepository(metadata); var executor = new ServiceOperationExecutor();
            var receipts = new ManagementIdentityOperationService(f.Data.Operation.Store, f.Data.Operation, f, Options.Create(f.Data.Operation.Options), f.Data.Operation.Clock, executor);
            var catalog = new ManagementAssignableRoleCatalogService(roleReads, f.Data.Tenants.Store, f, Options.Create(f.Policy), Options.Create(f.Data.Operation.Options), executor);
            var service = new ManagementCustomerProvisioningService(journal, receipts, catalog, external, inviteStore, new AzureTableManagementCustomerInvitationReadRepository(invitations),
                f.Data.Users.Object, f.Data.Memberships.Object, f.Data.Tenants.Store, f, Options.Create(f.Data.Operation.Options), f.Data.Operation.Clock, executor);
            var first = await service.ProvisionAsync(f.Tenant, f.Request, "provider-provision-01");
            var repeat = await service.ProvisionAsync(f.Tenant, f.Request, "provider-provision-02");
            Assert.AreEqual("sent", first.Status); Assert.AreEqual(first.Invitation!.InviteId, repeat.Invitation!.InviteId); Assert.HasCount(1, sent);
            Assert.AreEqual(Guid.Empty, (await inviteStore.GetAsync(f.Tenant, first.Invitation.InviteId))!.InvitedByUserId);
            Assert.IsTrue((await inviteStore.GetAsync(f.Tenant, first.Invitation.InviteId))!.RequirePasswordSetup);
            Assert.AreEqual(before, (await roles.GetEntityAsync<TableEntity>(roleEntity.PartitionKey, roleEntity.RowKey)).Value.ETag);
            var scope = new ManagementTenantAdministrationScope(f.Data.Operation.Actor.IdentityRealmId, "atlas", "Testing");
            var saved = (await journal.GetForOperationAsync(scope, f.Tenant, first.Operation.OperationId))!;
            var next = await journal.ReplaceAsync(saved with { DeliveryStatus = "sent" }, saved.Version);
            await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => journal.ReplaceAsync(saved, saved.Version));
            Assert.IsNull(await journal.GetAsync(scope with { IdentityRealmId = Guid.NewGuid() }, f.Tenant, saved.InviteId));
            Assert.IsNull(await journal.GetAsync(scope, Guid.NewGuid(), saved.InviteId));
            await foreach (var row in metadata.QueryAsync<TableEntity>()) {
                var text = JsonSerializer.Serialize(row); Assert.DoesNotContain(f.Request.Email, text); Assert.DoesNotContain(sent[0].Code!, text);
            }
            Assert.DoesNotContain(sent[0].Code!, JsonSerializer.Serialize(first));
            f.Data.Users.Verify(x => x.CreateAsync(It.IsAny<RegisterUserRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        } finally { await roles.DeleteAsync(); await invitations.DeleteAsync(); await metadata.DeleteAsync(); }
    }
}
