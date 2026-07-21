using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Invites;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class TenantInviteServiceTests
{
    [TestMethod]
    public async Task CreateInviteAsync_SendsInvite_WithoutResolvingDestinationUser()
    {
        var tenantId = Guid.NewGuid();
        var invitedBy = Guid.NewGuid();
        var sender = new RecordingIdentitySender();
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var sut = CreateService(sender: sender, users: users.Object, tenantId: tenantId);

        var result = await sut.CreateInviteAsync(
            tenantId,
            new TenantInviteCreateRequest(
                TenantInviteDestinationTypes.Email,
                Email: " Invited@Example.com ",
                DisplayName: "Invited Person",
                RoleNames: ["Member"],
                RedirectUrl: "https://app.example.com/invites"),
            invitedBy);

        Assert.AreEqual(tenantId, result.Invite.TenantId);
        Assert.AreEqual("invited@example.com", result.Invite.NormalizedDestination);
        Assert.AreEqual(TenantInviteStatuses.Sent, result.Invite.Status);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.InviteToken));
        Assert.Contains("inviteToken=", result.InviteUrl);
        Assert.HasCount(1, sender.Messages);
        Assert.AreEqual(SenderPurpose.TenantInvitation, sender.Messages[0].Purpose);
        users.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task AcceptInviteAsync_WithExistingSession_LinksExistingUserAndEnsuresExtension()
    {
        var tenantId = Guid.NewGuid();
        var invitedBy = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var user = new IdentityUser(userId, "invited@example.com", true, PhoneNumber: "+16145551212", DisplayName: "Existing User");
        var sender = new RecordingIdentitySender();
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var roles = new Mock<ITenantRoleService>(MockBehavior.Strict);
        var memberships = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var extensions = new Mock<IIdentityUserExtensionCoordinator>(MockBehavior.Strict);
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);

        users.Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        roles.Setup(x => x.EnsureTenantMembershipAndGrantRolesAsync(
                It.Is<TenantMembershipRoleBootstrapRequest>(r =>
                    r.TenantId == tenantId &&
                    r.UserId == userId &&
                    r.RoleNames!.SequenceEqual(new[] { "Member" }) &&
                    r.SetAsDefault &&
                    r.UserDisplayName == "Invite Name" &&
                    r.UserEmail == "invited@example.com" &&
                    r.UserPhoneNumber == "+16145551212"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserTenantRoleAssignment(
                tenantId,
                userId,
                [new TenantRole(tenantId, roleId, "Member", false, true, DateTimeOffset.UtcNow)]));

        memberships.Setup(x => x.GetTenantForUserAsync(userId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantInfo(tenantId, "Workspace", ["Member"], true, [roleId]));

        extensions.Setup(x => x.EnsureExtensionAsync(
                It.Is<IdentityUser>(u => u.UserId == userId && u.DisplayName == "Invite Name"),
                It.Is<UserExtensionContext>(c =>
                    c.Operation == "invite-accepted" &&
                    c.TenantId == tenantId &&
                    c.UserId == userId &&
                    c.NormalizedEmail == "invited@example.com" &&
                    c.DisplayName == "Invite Name" &&
                    c.FirstName == "Ada" &&
                    c.Metadata!["source"] == "test"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        tokens.Setup(x => x.CreateAccessTokenAsync(
                userId,
                tenantId,
                It.Is<IReadOnlyList<ClaimItem>>(claims =>
                    claims.Any(c => c.Type == "tid" && c.Value == tenantId.ToString("D")) &&
                    claims.Any(c => c.Type == "role" && c.Value == "Member") &&
                    claims.Any(c => c.Type == "rid" && c.Value == roleId.ToString("D"))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResult("jwt", DateTimeOffset.UtcNow.AddHours(1), []));

        var sut = CreateService(
            sender: sender,
            users: users.Object,
            roles: roles.Object,
            memberships: memberships.Object,
            extensions: extensions.Object,
            tokens: tokens.Object,
            tenantId: tenantId);

        var created = await sut.CreateInviteAsync(
            tenantId,
            new TenantInviteCreateRequest(
                TenantInviteDestinationTypes.Email,
                Email: "invited@example.com",
                DisplayName: "Invite Name",
                FirstName: "Ada",
                RoleNames: ["Member"],
                SetAsDefaultTenant: true,
                Metadata: new Dictionary<string, string> { ["source"] = "test" }),
            invitedBy);

        var accepted = await sut.AcceptInviteAsync(
            new TenantInviteAcceptRequest(
                InviteToken: created.InviteToken,
                Mode: TenantInviteAcceptModes.ExistingSession),
            userId);

        Assert.AreEqual(TenantInviteStatuses.Redeemed, accepted.Invite.Status);
        Assert.AreEqual(userId, accepted.User.UserId);
        Assert.IsFalse(accepted.CreatedNewUser);
        Assert.IsNotNull(accepted.Token);
        users.VerifyAll();
        roles.VerifyAll();
        memberships.VerifyAll();
        extensions.VerifyAll();
        tokens.VerifyAll();
    }

    private static TenantInviteService CreateService(
        RecordingIdentitySender? sender = null,
        IIdentityUserStore? users = null,
        ITenantRoleService? roles = null,
        ITenantMembershipStore? memberships = null,
        IIdentityUserExtensionCoordinator? extensions = null,
        ITokenService? tokens = null,
        Guid? tenantId = null)
    {
        var effectiveTenantId = tenantId ?? Guid.NewGuid();
        var tenants = new Mock<IIdentityTenantService>(MockBehavior.Strict);
        tenants.Setup(x => x.FindByIdAsync(effectiveTenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityTenant(effectiveTenantId, "Workspace", "WORKSPACE"));

        return new TenantInviteService(
            new InMemoryTenantInviteStore(),
            tenants.Object,
            users ?? Mock.Of<IIdentityUserStore>(MockBehavior.Strict),
            roles ?? Mock.Of<ITenantRoleService>(MockBehavior.Strict),
            Mock.Of<ITenantProvisioningService>(MockBehavior.Strict),
            memberships ?? Mock.Of<ITenantMembershipStore>(MockBehavior.Strict),
            extensions ?? Mock.Of<IIdentityUserExtensionCoordinator>(MockBehavior.Strict),
            sender ?? new RecordingIdentitySender(),
            new DefaultTenantInviteUrlBuilder(),
            new DefaultTenantInviteMessageFactory(),
            Mock.Of<IOtpService>(MockBehavior.Strict),
            Mock.Of<IOtpChallengeStore>(MockBehavior.Strict),
            tokens ?? Mock.Of<ITokenService>(MockBehavior.Strict),
            new NoOpAuthEventPublisher(),
            Options.Create(new AuthEventOptions()),
            NullLogger<TenantInviteService>.Instance);
    }

    private sealed class RecordingIdentitySender : IIdentityCommunicationSender
    {
        public List<IdentitySenderMessage> Messages { get; } = [];

        public Task SendAsync(IdentitySenderMessage message, CancellationToken ct = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
