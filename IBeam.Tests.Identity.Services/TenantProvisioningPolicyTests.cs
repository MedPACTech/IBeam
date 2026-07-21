using IBeam.Identity.Exceptions;
using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class TenantProvisioningPolicyTests
{
    [TestMethod]
    public async Task CompleteOtpAsync_RequireExistingTenant_DoesNotCreateTenant_WhenUserHasNoMembership()
    {
        var userId = Guid.NewGuid();
        var defaultTenantId = Guid.NewGuid();
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var tenantProvisioning = new Mock<ITenantProvisioningService>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var otpChallenges = new Mock<IOtpChallengeStore>(MockBehavior.Strict);

        otpService.Setup(x => x.VerifyAsync(It.IsAny<OtpVerifyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerifyResult(true, "vt", DateTimeOffset.UtcNow.AddMinutes(10)));

        otpChallenges.Setup(x => x.GetAsync("challenge-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpChallengeRecord(
                "challenge-1",
                "+16142649686",
                SenderPurpose.LoginMfa,
                "hash",
                DateTimeOffset.UtcNow.AddMinutes(10),
                0,
                defaultTenantId,
                true,
                "vt",
                DateTimeOffset.UtcNow.AddMinutes(10),
                SenderChannel.Sms));

        users.Setup(x => x.FindByPhoneAsync("+16142649686", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUser(userId, string.Empty, false, "+16142649686"));

        tenants.Setup(x => x.GetTenantsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TenantInfo>());

        var sut = CreateOtpService(
            users.Object,
            tenants.Object,
            tenantProvisioning.Object,
            Mock.Of<ITokenService>(),
            otpService.Object,
            otpChallenges.Object,
            new TenantProvisioningOptions
            {
                Mode = TenantProvisioningMode.RequireExistingTenant,
                DefaultTenantId = defaultTenantId
            });

        await Assert.ThrowsExactlyAsync<IdentityValidationException>(() =>
            sut.CompleteOtpAsync("challenge-1", "123456", "16142649686"));

        tenantProvisioning.Verify(
            x => x.CreateTenantForNewUserAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        tenantProvisioning.Verify(
            x => x.EnsureUserTenantMembershipAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task StartOtpAsync_UseDefaultTenant_AttachesDefaultTenantToChallenge()
    {
        var defaultTenantId = Guid.NewGuid();
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);

        users.Setup(x => x.FindByPhoneAsync("+16142649686", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUser(Guid.NewGuid(), string.Empty, false, "+16142649686"));

        otpService.Setup(x => x.CreateChallengeAsync(
                It.Is<OtpChallengeRequest>(r =>
                    r.Channel == SenderChannel.Sms &&
                    r.Destination == "+16142649686" &&
                    r.TenantId == defaultTenantId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpChallengeResult("otp-1", DateTimeOffset.UtcNow.AddMinutes(10)));

        var sut = CreateOtpService(
            users.Object,
            Mock.Of<ITenantMembershipStore>(),
            Mock.Of<ITenantProvisioningService>(),
            Mock.Of<ITokenService>(),
            otpService.Object,
            Mock.Of<IOtpChallengeStore>(),
            new TenantProvisioningOptions
            {
                Mode = TenantProvisioningMode.UseDefaultTenant,
                DefaultTenantId = defaultTenantId
            });

        await sut.StartOtpAsync("16142649686");

        otpService.VerifyAll();
    }

    [TestMethod]
    public async Task CompleteOtpAsync_UseDefaultTenant_AutoLinksAndIssuesToken()
    {
        var userId = Guid.NewGuid();
        var defaultTenantId = Guid.NewGuid();
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var tenantProvisioning = new Mock<ITenantProvisioningService>(MockBehavior.Strict);
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var otpChallenges = new Mock<IOtpChallengeStore>(MockBehavior.Strict);

        otpService.Setup(x => x.VerifyAsync(It.IsAny<OtpVerifyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerifyResult(true, "vt", DateTimeOffset.UtcNow.AddMinutes(10)));

        otpChallenges.Setup(x => x.GetAsync("challenge-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpChallengeRecord(
                "challenge-1",
                "+16142649686",
                SenderPurpose.LoginMfa,
                "hash",
                DateTimeOffset.UtcNow.AddMinutes(10),
                0,
                defaultTenantId,
                true,
                "vt",
                DateTimeOffset.UtcNow.AddMinutes(10),
                SenderChannel.Sms));

        users.Setup(x => x.FindByPhoneAsync("+16142649686", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUser(userId, string.Empty, false, "+16142649686"));
        users.Setup(x => x.SetPhoneConfirmedAsync(userId, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        tenants.SetupSequence(x => x.GetTenantsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TenantInfo>())
            .ReturnsAsync(new[] { new TenantInfo(defaultTenantId, "Wellderly", Array.Empty<string>(), true) });

        tenantProvisioning.Setup(x => x.EnsureUserTenantMembershipAsync(
                defaultTenantId,
                userId,
                null,
                It.Is<IReadOnlyList<string>?>(roles => roles != null && roles.SequenceEqual(new[] { "Member" })),
                true,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        tokens.Setup(x => x.CreateAccessTokenAsync(
                userId,
                defaultTenantId,
                It.IsAny<IReadOnlyList<ClaimItem>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResult("jwt-token", DateTimeOffset.UtcNow.AddMinutes(60), Array.Empty<ClaimItem>()));

        var sut = CreateOtpService(
            users.Object,
            tenants.Object,
            tenantProvisioning.Object,
            tokens.Object,
            otpService.Object,
            otpChallenges.Object,
            new TenantProvisioningOptions
            {
                Mode = TenantProvisioningMode.UseDefaultTenant,
                DefaultTenantId = defaultTenantId,
                AutoLinkUserToDefaultTenant = true,
                AutoLinkRoleNames = new List<string> { "Member" }
            });

        var result = await sut.CompleteOtpAsync("challenge-1", "123456", "+16142649686");

        Assert.IsNotNull(result.Token);
        Assert.AreEqual("jwt-token", result.Token!.AccessToken);
        tenantProvisioning.Verify(
            x => x.CreateTenantForNewUserAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        tenantProvisioning.VerifyAll();
    }

    private static OtpAuthService CreateOtpService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IOtpService otpService,
        IOtpChallengeStore otpChallenges,
        TenantProvisioningOptions tenantProvisioningOptions)
        => new(
            users,
            tenants,
            tenantProvisioning,
            tokens,
            otpService,
            otpChallenges,
            new NoOpAuthEventPublisher(),
            new NoOpAuthLifecycleHook(),
            Options.Create(new AuthEventOptions()),
            Options.Create(new OtpOptions { AllowAutoProvisionForUnknownUser = true }),
            Options.Create(tenantProvisioningOptions),
            NullLogger<OtpAuthService>.Instance);
}
