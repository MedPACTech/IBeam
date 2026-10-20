using System.Security.Cryptography;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Invites;
using Moq;
namespace IBeam.Tests.Identity.Services;

public sealed partial class TenantInviteServiceTests
{
    [TestMethod]
    public async Task ExternalInviteHasStableIdentityNoCustomerInviterAndUsesExistingApprovedDelivery()
    {
        var tenant = Guid.NewGuid(); var role = new TenantRole(tenant, Guid.NewGuid(), "Owner", true, true, DateTimeOffset.UtcNow);
        var roles = new Mock<ITenantRoleService>(MockBehavior.Strict);
        roles.Setup(x => x.GetRoleAsync(tenant, role.RoleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        var store = new ExternalMemoryInvites(); var sender = new RecordingIdentitySender();
        var service = CreateService(store, sender, roles: roles.Object, tenantId: tenant);
        var request = ExternalRequest(tenant, role.RoleId);
        var one = await service.EnsureAsync(request); var two = await service.EnsureAsync(request);
        Assert.AreEqual(one.InviteId, two.InviteId); Assert.HasCount(1, await store.ListForTenantAsync(tenant));
        Assert.AreEqual(Guid.Empty, one.InvitedByUserId); Assert.IsTrue(one.RequirePasswordSetup); Assert.HasCount(0, sender.Messages);
        Assert.AreEqual(request.Actor.UserId.ToString("D"), one.Metadata["support_user_id"]);
        await service.DeliverAsync(tenant, request.InviteId, request.Actor.OperationId, request.Token);
        Assert.HasCount(1, sender.Messages); Assert.AreEqual(request.Token, sender.Messages[0].Code);
        Assert.Contains("https://localhost:3000/invites/accept?inviteToken=", sender.Messages[0].Body!);
        Assert.AreEqual("pending", (await store.GetAsync(tenant, request.InviteId))!.Status);
        Assert.DoesNotContain(request.Token, System.Text.Json.JsonSerializer.Serialize(request));
        Assert.DoesNotContain(request.Email, request.ToString());
    }
    [TestMethod]
    [DataRow("Member", true)] [DataRow("SupportAdmin", true)] [DataRow("*", true)] [DataRow("Owner", false)]
    public async Task ExternalInvitesRejectUnapprovedOrInactiveCustomerRoles(string name, bool active)
    {
        var tenant = Guid.NewGuid(); var role = Guid.NewGuid(); var roles = new Mock<ITenantRoleService>();
        roles.Setup(x => x.GetRoleAsync(tenant, role, It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRole(tenant, role, name, true, active, DateTimeOffset.UtcNow));
        var store = new ExternalMemoryInvites(); var service = CreateService(store: store, roles: roles.Object, tenantId: tenant);
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.EnsureAsync(ExternalRequest(tenant, role)));
        Assert.HasCount(0, await store.ListForTenantAsync(tenant));
    }
    [TestMethod]
    public async Task ExternalInvitationsStillRequireVerifiedRecipientAndPasswordChallengeBeforeAnyMembership()
    {
        var tenant = Guid.NewGuid(); var role = Guid.NewGuid(); var userId = Guid.NewGuid();
        var roles = new Mock<ITenantRoleService>(MockBehavior.Strict);
        roles.Setup(x => x.GetRoleAsync(tenant, role, It.IsAny<CancellationToken>())).ReturnsAsync(new TenantRole(tenant, role, "Owner", true, true, DateTimeOffset.UtcNow));
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        users.Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new IdentityUser(userId, "enterprise@example.test", false));
        var store = new ExternalMemoryInvites(); var service = CreateService(store: store, users: users.Object, roles: roles.Object, tenantId: tenant);
        var request = ExternalRequest(tenant, role); await service.EnsureAsync(request);
        await Assert.ThrowsAsync<IdentityUnauthorizedException>(() => service.AcceptInviteAsync(new(InviteToken: request.Token, Mode: "existing-session"), userId));
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.AcceptInviteAsync(new(InviteToken: request.Token, Mode: "email-password", Password: "NotStoredPassword123!")));
        Assert.AreEqual("pending", (await store.GetAsync(tenant, request.InviteId))!.Status);
        roles.Verify(x => x.EnsureTenantMembershipAndGrantRolesAsync(It.IsAny<TenantMembershipRoleBootstrapRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        users.Verify(x => x.SetPasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [TestMethod]
    public async Task ExternalInviteVerifiedExistingCustomerAcceptsThroughOrdinaryMembershipAndExtensionFlow()
    {
        var tenant = Guid.NewGuid(); var user = new IdentityUser(Guid.NewGuid(), "enterprise@example.test", true);
        var role = new TenantRole(tenant, Guid.NewGuid(), "Owner", true, true, DateTimeOffset.UtcNow);
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        users.Setup(x => x.FindByIdAsync(user.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var roles = new Mock<ITenantRoleService>(MockBehavior.Strict);
        roles.Setup(x => x.GetRoleAsync(tenant, role.RoleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        roles.Setup(x => x.EnsureTenantMembershipAndGrantRolesAsync(It.Is<TenantMembershipRoleBootstrapRequest>(r =>
            r.TenantId == tenant && r.UserId == user.UserId && r.RoleIds!.SequenceEqual(new[] { role.RoleId }) && !r.SetAsDefault), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserTenantRoleAssignment(tenant, user.UserId, [role]));
        var memberships = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        memberships.Setup(x => x.GetTenantForUserAsync(user.UserId, tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new TenantInfo(tenant, "Workspace", ["Owner"], true, [role.RoleId]));
        var extensions = new Mock<IIdentityUserExtensionCoordinator>(MockBehavior.Strict);
        extensions.Setup(x => x.EnsureExtensionAsync(user, It.Is<UserExtensionContext>(c => c.Operation == "invite-accepted" && c.TenantId == tenant), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        tokens.Setup(x => x.CreateAccessTokenAsync(user.UserId, tenant, It.IsAny<IReadOnlyList<ClaimItem>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TokenResult("customer-token", DateTimeOffset.UtcNow.AddHours(1), []));
        var service = CreateService(new ExternalMemoryInvites(), users: users.Object, roles: roles.Object, memberships: memberships.Object, extensions: extensions.Object, tokens: tokens.Object, tenantId: tenant);
        var request = ExternalRequest(tenant, role.RoleId); await service.EnsureAsync(request);
        roles.Verify(x => x.EnsureTenantMembershipAndGrantRolesAsync(It.IsAny<TenantMembershipRoleBootstrapRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var accepted = await service.AcceptInviteAsync(new(InviteToken: request.Token, Mode: "existing-session"), user.UserId);
        Assert.AreEqual("redeemed", accepted.Invite.Status); Assert.AreEqual(user.UserId, accepted.Invite.RedeemedByUserId);
        Assert.IsFalse(accepted.CreatedNewUser); Assert.AreEqual("Owner", accepted.Roles.Single().Name);
        users.VerifyAll(); users.VerifyNoOtherCalls();
    }
    private static ExternalTenantInviteRequest ExternalRequest(Guid tenant, Guid role) => new() {
        TenantId = tenant, InviteId = Guid.NewGuid(), Actor = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
        Email = "enterprise@example.test", Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        RoleIds = [role], CreatedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(6) };
    private sealed class ExternalMemoryInvites : ITenantInviteStore, IExternalTenantInviteStore
    {
        private readonly InMemoryTenantInviteStore store = new();
        public async Task<TenantInviteRecord> EnsureAsync(TenantInviteRecord invite, CancellationToken ct = default)
            => await store.GetAsync(invite.TenantId, invite.InviteId, ct) ?? await store.CreateAsync(invite, ct);
        public Task<TenantInviteRecord> CreateAsync(TenantInviteRecord invite, CancellationToken ct = default) => store.CreateAsync(invite, ct);
        public Task<IReadOnlyList<TenantInviteRecord>> ListForTenantAsync(Guid tenant, CancellationToken ct = default) => store.ListForTenantAsync(tenant, ct);
        public Task<TenantInviteRecord?> GetAsync(Guid tenant, Guid invite, CancellationToken ct = default) => store.GetAsync(tenant, invite, ct);
        public Task<TenantInviteRecord?> FindByTokenHashAsync(string hash, CancellationToken ct = default) => store.FindByTokenHashAsync(hash, ct);
        public Task<TenantInviteRecord> UpdateAsync(TenantInviteRecord invite, CancellationToken ct = default) => store.UpdateAsync(invite, ct);
    }
}
