using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Tenants;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class TenantUserProvisioningServiceTests
{
    [TestMethod]
    public async Task ProvisionAsync_CreatesUserLinksRolesInvokesExtensionAndSendsSetupInvite()
    {
        var tenantId = Guid.NewGuid();
        var provisionedBy = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var user = new IdentityUser(userId, "ada@example.com", false, DisplayName: "Ada Lovelace");
        var role = new TenantRole(tenantId, roleId, "Member", false, true, DateTimeOffset.UtcNow);
        var tenants = new Mock<IIdentityTenantService>(MockBehavior.Strict);
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var roles = new Mock<ITenantRoleService>(MockBehavior.Strict);
        var tenantProvisioning = new Mock<ITenantProvisioningService>(MockBehavior.Strict);
        var memberships = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var extensions = new Mock<IIdentityUserExtensionCoordinator>(MockBehavior.Strict);
        var invites = new Mock<ITenantInviteService>(MockBehavior.Strict);

        tenants.Setup(x => x.FindByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityTenant(tenantId, "Workspace", "WORKSPACE"));
        users.Setup(x => x.FindByEmailAsync("ada@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUser?)null);
        users.Setup(x => x.CreateAsync(
                It.Is<RegisterUserRequest>(r =>
                    r.Email == "ada@example.com" &&
                    r.Password == string.Empty &&
                    r.DisplayName == "Ada Lovelace"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateUserResult.Success(user));
        users.Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        roles.Setup(x => x.EnsureTenantMembershipAndGrantRolesAsync(
                It.Is<TenantMembershipRoleBootstrapRequest>(r =>
                    r.TenantId == tenantId &&
                    r.UserId == userId &&
                    r.SetAsDefault &&
                    r.RoleNames!.SequenceEqual(new[] { "Member" }) &&
                    r.UserDisplayName == "Ada Lovelace"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserTenantRoleAssignment(tenantId, userId, [role]));

        memberships.Setup(x => x.GetTenantForUserAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantInfo(tenantId, "Workspace", ["Member"], true, [roleId]));

        extensions.Setup(x => x.EnsureExtensionAsync(
                It.Is<IdentityUser>(u => u.UserId == userId && u.DisplayName == "Ada Lovelace"),
                It.Is<UserExtensionContext>(c =>
                    c.Operation == UserExtensionOperations.AdminProvisioned &&
                    c.UserId == userId &&
                    c.TenantId == tenantId &&
                    c.NormalizedEmail == "ada@example.com" &&
                    c.FirstName == "Ada" &&
                    c.LastName == "Lovelace" &&
                    c.Metadata!["employeeId"] == "E-100"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        invites.Setup(x => x.CreateInviteAsync(
                tenantId,
                It.Is<TenantInviteCreateRequest>(r =>
                    r.Email == "ada@example.com" &&
                    r.DisplayName == "Ada Lovelace" &&
                    r.RequirePasswordSetup &&
                    r.RoleNames!.SequenceEqual(new[] { "Member" })),
                provisionedBy,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantInviteCreatedResult(
                new TenantInviteInfo(
                    Guid.NewGuid(),
                    tenantId,
                    TenantInviteDestinationTypes.Email,
                    "ada@example.com",
                    TenantInviteStatuses.Sent,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddDays(7),
                    provisionedBy,
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    null,
                    null,
                    null,
                    new TenantInviteProfileHints("Ada Lovelace", "Ada", "Lovelace"),
                    [],
                    ["Member"],
                    true,
                    [],
                    null,
                    new Dictionary<string, string>(),
                    true),
                "token",
                "https://app.example.com/invites/accept?inviteToken=token"));

        var sut = new TenantUserProvisioningService(
            tenants.Object,
            users.Object,
            roles.Object,
            tenantProvisioning.Object,
            memberships.Object,
            extensions.Object,
            invites.Object);

        var result = await sut.ProvisionAsync(
            tenantId,
            new ProvisionTenantUserRequest(
                Email: " Ada@Example.com ",
                DisplayName: "Ada Lovelace",
                FirstName: "Ada",
                LastName: "Lovelace",
                RoleNames: ["Member"],
                SetAsDefaultTenant: true,
                SendInvite: true,
                RequirePasswordSetup: true,
                ProfileMetadata: new Dictionary<string, string> { ["employeeId"] = "E-100" }),
            provisionedBy);

        Assert.IsTrue(result.CreatedNewUser);
        Assert.IsNotNull(result.Invite);
        Assert.AreEqual(userId, result.User.UserId);
        tenants.VerifyAll();
        users.VerifyAll();
        roles.VerifyAll();
        tenantProvisioning.VerifyNoOtherCalls();
        memberships.VerifyAll();
        extensions.VerifyAll();
        invites.VerifyAll();
    }
}
