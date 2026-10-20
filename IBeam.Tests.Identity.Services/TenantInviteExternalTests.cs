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
    [DataRow("existing-session", false)] [DataRow("email-password", false)] [DataRow("email-password", true)] [DataRow("otp", false)]
    public async Task ExternalInviteVerifiedCustomerAcceptsThroughOrdinaryMembershipAndExtensionFlow(string mode, bool newCustomer)
    {
        var tenant = Guid.NewGuid(); var user = new IdentityUser(Guid.NewGuid(), "enterprise@example.test", true);
        var role = new TenantRole(tenant, Guid.NewGuid(), "Owner", true, true, DateTimeOffset.UtcNow);
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        users.Setup(x => x.FindByIdAsync(user.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var challenges = new Mock<IOtpChallengeStore>(MockBehavior.Strict);
        var otp = new Mock<IOtpService>(MockBehavior.Strict);
        if (mode == "otp") {
            users.Setup(x => x.FindByEmailAsync(user.Email!, It.IsAny<CancellationToken>())).ReturnsAsync(user with { EmailConfirmed = false });
            users.Setup(x => x.SetEmailConfirmedAsync(user.UserId, true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            otp.Setup(x => x.VerifyAsync(It.Is<OtpVerifyRequest>(r => r.ChallengeId == "verified-email-challenge"), It.IsAny<CancellationToken>())).ReturnsAsync(new OtpVerifyResult(true));
            challenges.Setup(x => x.GetAsync("verified-email-challenge", It.IsAny<CancellationToken>())).ReturnsAsync(new OtpChallengeRecord("verified-email-challenge",
                user.Email!, SenderPurpose.TenantInvitation, "hash", DateTimeOffset.UtcNow.AddMinutes(5), 0, tenant, true, "verified-email-proof", DateTimeOffset.UtcNow.AddMinutes(5)));
        }
        if (mode == "email-password") {
            users.Setup(x => x.FindByEmailAsync(user.Email!, It.IsAny<CancellationToken>())).ReturnsAsync(newCustomer ? null : user with { EmailConfirmed = false });
            users.Setup(x => x.SetEmailConfirmedAsync(user.UserId, true, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            if (newCustomer) users.Setup(x => x.CreateAsync(It.Is<RegisterUserRequest>(r => r.Email == user.Email), It.IsAny<CancellationToken>())).ReturnsAsync(CreateUserResult.Success(user with { EmailConfirmed = false }));
            else users.Setup(x => x.SetPasswordAsync(user.UserId, "CustomerPassword123!", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            challenges.Setup(x => x.GetAsync("verified-email-challenge", It.IsAny<CancellationToken>())).ReturnsAsync(new OtpChallengeRecord("verified-email-challenge",
                user.Email!, SenderPurpose.TenantInvitation, "hash", DateTimeOffset.UtcNow.AddMinutes(5), 0, tenant, true, "verified-email-proof", DateTimeOffset.UtcNow.AddMinutes(5)));
        }
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
        var service = CreateService(new ExternalMemoryInvites(), users: users.Object, roles: roles.Object, memberships: memberships.Object, extensions: extensions.Object, tokens: tokens.Object, tenantId: tenant, otpChallenges: challenges.Object, otp: otp.Object);
        var request = ExternalRequest(tenant, role.RoleId); await service.EnsureAsync(request);
        roles.Verify(x => x.EnsureTenantMembershipAndGrantRolesAsync(It.IsAny<TenantMembershipRoleBootstrapRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var accepted = await service.AcceptInviteAsync(new(InviteToken: request.Token, Mode: mode,
            ChallengeId: mode is "email-password" or "otp" ? "verified-email-challenge" : null, Code: mode == "otp" ? "123456" : null,
            VerificationToken: mode == "email-password" ? "verified-email-proof" : null, Password: "CustomerPassword123!"), mode == "existing-session" ? user.UserId : null);
        Assert.AreEqual("redeemed", accepted.Invite.Status); Assert.AreEqual(user.UserId, accepted.Invite.RedeemedByUserId);
        Assert.AreEqual(newCustomer, accepted.CreatedNewUser); Assert.AreEqual("Owner", accepted.Roles.Single().Name);
        Assert.IsTrue(accepted.User.EmailConfirmed);
        users.Verify(x => x.CreateAsync(It.IsAny<RegisterUserRequest>(), It.IsAny<CancellationToken>()), newCustomer ? Times.Once() : Times.Never());
    }
    private static ExternalTenantInviteRequest ExternalRequest(Guid tenant, Guid role) => new() {
        TenantId = tenant, InviteId = Guid.NewGuid(), Actor = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
        Email = "enterprise@example.test", Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        RoleIds = [role], CreatedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(6) };
    [TestMethod]
    public async Task DisabledTenantCannotCreateResendOrAcceptInvitationsOrReachCustomerAccountSetup()
    {
        var tenant = Guid.NewGuid(); var store = new ExternalMemoryInvites(); var sender = new RecordingIdentitySender();
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var service = CreateService(store, sender, users: users.Object, tenantId: tenant, tenantActive: false);
        var request = ExternalRequest(tenant, Guid.NewGuid());
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.EnsureAsync(request));
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.CreateInviteAsync(tenant, new("email", Email: request.Email), Guid.NewGuid()));
        await store.CreateAsync(new(request.InviteId, tenant, "email", request.Email,
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Token))), "pending", request.CreatedUtc,
            Guid.NewGuid(), request.ExpiresUtc, RoleIds: request.RoleIds));
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.ResendInviteAsync(tenant, request.InviteId, Guid.NewGuid()));
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.AcceptInviteAsync(new(InviteToken: request.Token, Mode: "email-password", Password: "Private123!")));
        Assert.HasCount(0, sender.Messages); Assert.AreEqual(0, store.Claims);
        users.Verify(x => x.CreateAsync(It.IsAny<RegisterUserRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        users.Verify(x => x.SetPasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [TestMethod]
    public async Task AcceptanceMustClaimTheCurrentInvitationBeforeMembershipOrTokenCreation()
    {
        var tenant = Guid.NewGuid(); var user = new IdentityUser(Guid.NewGuid(), "enterprise@example.test", true);
        var roles = new Mock<ITenantRoleService>(MockBehavior.Strict);
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        users.Setup(x => x.FindByIdAsync(user.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var store = new ExternalMemoryInvites { FailClaim = true };
        var request = ExternalRequest(tenant, Guid.NewGuid());
        await store.CreateAsync(new(request.InviteId, tenant, "email", request.Email,
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Token))), "pending", request.CreatedUtc, Guid.Empty,
            request.ExpiresUtc, RoleIds: request.RoleIds, Metadata: new Dictionary<string, string> { ["origin"] = "external-management" }));
        var service = CreateService(store, users: users.Object, roles: roles.Object, tenantId: tenant);
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => service.AcceptInviteAsync(new(InviteToken: request.Token, Mode: "existing-session"), user.UserId));
        Assert.AreEqual(1, store.Claims);
        roles.Verify(x => x.EnsureTenantMembershipAndGrantRolesAsync(It.IsAny<TenantMembershipRoleBootstrapRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [TestMethod]
    public async Task ManagementResentCustomerOriginInvitationAlsoRequiresVerifiedPasswordChallenge()
    {
        var tenant = Guid.NewGuid(); var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var row = new TenantInviteRecord(Guid.NewGuid(), tenant, "email", "customer@example.test",
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))), "pending", DateTimeOffset.UtcNow,
            Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(7), RoleIds: [Guid.NewGuid()], RequirePasswordSetup: true) { RequireVerifiedAcceptance = true };
        var store = new ExternalMemoryInvites(); await store.CreateAsync(row);
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var service = CreateService(store, users: users.Object, tenantId: tenant);
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.AcceptInviteAsync(new(InviteToken: token, Mode: "email-password", Password: "NotStored123!")));
        Assert.AreEqual(0, store.Claims);
        users.Verify(x => x.SetPasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    private sealed class ExternalMemoryInvites : ITenantInviteStore, IExternalTenantInviteStore, IManagedTenantInviteStore
    {
        private readonly InMemoryTenantInviteStore store = new();
        public bool FailClaim { get; set; }
        public int Claims { get; private set; }
        public async Task<TenantInviteRecord> BeginAcceptanceAsync(Guid tenant, Guid invite, string hash, Guid user, CancellationToken ct = default)
        {
            Claims++; if (FailClaim) throw new IdentityConcurrencyException();
            var row = await store.GetAsync(tenant, invite, ct) ?? throw new IdentityConcurrencyException();
            if (row.TokenHash != hash) throw new IdentityConcurrencyException();
            return await store.UpdateAsync(row with { AcceptanceId = Guid.NewGuid(), AcceptanceUserId = user }, ct);
        }
        public Task<TenantInviteRecord> TransitionAsync(ManagedTenantInviteTransition request, CancellationToken ct = default) => throw new NotSupportedException();
        public async Task<TenantInviteRecord> EnsureAsync(TenantInviteRecord invite, CancellationToken ct = default)
            => await store.GetAsync(invite.TenantId, invite.InviteId, ct) ?? await store.CreateAsync(invite, ct);
        public Task<TenantInviteRecord> CreateAsync(TenantInviteRecord invite, CancellationToken ct = default) => store.CreateAsync(invite, ct);
        public Task<IReadOnlyList<TenantInviteRecord>> ListForTenantAsync(Guid tenant, CancellationToken ct = default) => store.ListForTenantAsync(tenant, ct);
        public Task<TenantInviteRecord?> GetAsync(Guid tenant, Guid invite, CancellationToken ct = default) => store.GetAsync(tenant, invite, ct);
        public Task<TenantInviteRecord?> FindByTokenHashAsync(string hash, CancellationToken ct = default) => store.FindByTokenHashAsync(hash, ct);
        public Task<TenantInviteRecord> UpdateAsync(TenantInviteRecord invite, CancellationToken ct = default) => store.UpdateAsync(invite, ct);
    }
}
