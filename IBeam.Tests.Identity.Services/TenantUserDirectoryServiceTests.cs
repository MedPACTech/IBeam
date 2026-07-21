using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Tenants;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class TenantUserDirectoryServiceTests
{
    [TestMethod]
    public async Task ListAsync_Default_ReturnsActiveUsersOnly()
    {
        var tenantId = Guid.NewGuid();
        var memberships = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var invites = new Mock<ITenantInviteService>(MockBehavior.Strict);
        var roles = new Mock<ITenantRoleService>(MockBehavior.Strict);

        memberships.Setup(x => x.GetUsersForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TenantUserInfo(tenantId, Guid.NewGuid(), ["Member"], true, DisplayName: "Active"),
                new TenantUserInfo(tenantId, Guid.NewGuid(), ["Member"], false, DisplayName: "Disabled")
            ]);

        var sut = new TenantUserDirectoryService(memberships.Object, invites.Object, roles.Object);

        var list = await sut.ListAsync(tenantId);

        Assert.HasCount(1, list);
        Assert.AreEqual(TenantUserDirectoryItemKinds.User, list[0].Kind);
        Assert.AreEqual(TenantUserDirectoryStatuses.Active, list[0].Status);
        memberships.VerifyAll();
        invites.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ListAsync_PendingOnly_ReturnsActiveInviteRows()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var memberships = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var invites = new Mock<ITenantInviteService>(MockBehavior.Strict);
        var roles = new Mock<ITenantRoleService>(MockBehavior.Strict);

        invites.Setup(x => x.ListInvitesAsync(
                tenantId,
                It.Is<TenantInviteListRequest>(r => r.ActiveOnly),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TenantInviteInfo(
                    Guid.NewGuid(),
                    tenantId,
                    TenantInviteDestinationTypes.Email,
                    "pending@example.com",
                    TenantInviteStatuses.Sent,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(1),
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    null,
                    null,
                    null,
                    new TenantInviteProfileHints("Pending Person", "Pending", "Person"),
                    [roleId],
                    [],
                    true,
                    [],
                    null,
                    new Dictionary<string, string>())
            ]);
        roles.Setup(x => x.GetRolesAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TenantRole(tenantId, roleId, "Member", false, true, DateTimeOffset.UtcNow)]);

        var sut = new TenantUserDirectoryService(memberships.Object, invites.Object, roles.Object);

        var list = await sut.ListAsync(tenantId, new TenantUserDirectoryRequest(PendingOnly: true));

        Assert.HasCount(1, list);
        Assert.AreEqual(TenantUserDirectoryItemKinds.Invite, list[0].Kind);
        Assert.AreEqual("pending@example.com", list[0].Email);
        CollectionAssert.Contains(list[0].RoleNames!.ToList(), "Member");
        memberships.VerifyNoOtherCalls();
        invites.VerifyAll();
        roles.VerifyAll();
    }
}
