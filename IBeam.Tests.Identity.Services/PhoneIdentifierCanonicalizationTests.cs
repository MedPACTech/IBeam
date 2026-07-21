using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Auth;
using IBeam.Identity.Services.Utils;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class PhoneIdentifierCanonicalizationTests
{
    [TestMethod]
    [DataRow("6142649686")]
    [DataRow("16142649686")]
    [DataRow("+16142649686")]
    [DataRow("(614) 264-9686")]
    public void NormalizeDestination_UsPhoneFormats_ReturnsE164(string input)
    {
        var (channel, normalized) = IdentityUtils.NormalizeDestination(input);

        Assert.AreEqual(SenderChannel.Sms, channel);
        Assert.AreEqual("+16142649686", normalized);
    }

    [TestMethod]
    [DataRow("6142649686")]
    [DataRow("16142649686")]
    [DataRow("+16142649686")]
    [DataRow("(614) 264-9686")]
    public async Task StartOtpAsync_UsesCanonicalPhoneForLookupAndChallenge(string input)
    {
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        users.Setup(x => x.FindByPhoneAsync("+16142649686", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUser(Guid.NewGuid(), string.Empty, false, "+16142649686"));

        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        otpService.Setup(x => x.CreateChallengeAsync(
                It.Is<OtpChallengeRequest>(r =>
                    r.Channel == SenderChannel.Sms &&
                    r.Destination == "+16142649686"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpChallengeResult("otp-1", DateTimeOffset.UtcNow.AddMinutes(10)));

        var sut = new OtpAuthService(
            users.Object,
            Mock.Of<ITenantMembershipStore>(),
            Mock.Of<ITenantProvisioningService>(),
            Mock.Of<ITokenService>(),
            otpService.Object,
            Mock.Of<IOtpChallengeStore>());

        await sut.StartOtpAsync(input);

        users.VerifyAll();
        otpService.VerifyAll();
    }

    [TestMethod]
    public async Task CompleteOtpAsync_MatchesFormattedPhoneAgainstCanonicalChallengeDestination()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
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
                null,
                true,
                "vt",
                DateTimeOffset.UtcNow.AddMinutes(10),
                SenderChannel.Sms));

        users.Setup(x => x.FindByPhoneAsync("+16142649686", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUser(userId, string.Empty, false, "+16142649686"));
        users.Setup(x => x.SetPhoneConfirmedAsync(userId, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        tenants.Setup(x => x.GetTenantsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInfo> { new(tenantId, "Tenant A", new[] { "User" }, true) });

        tokens.Setup(x => x.CreateAccessTokenAsync(
                userId,
                tenantId,
                It.IsAny<IReadOnlyList<ClaimItem>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResult("jwt-token", DateTimeOffset.UtcNow.AddMinutes(60), Array.Empty<ClaimItem>()));

        var sut = new OtpAuthService(
            users.Object,
            tenants.Object,
            Mock.Of<ITenantProvisioningService>(),
            tokens.Object,
            otpService.Object,
            otpChallenges.Object);

        var result = await sut.CompleteOtpAsync("challenge-1", "123456", "(614) 264-9686");

        Assert.IsNotNull(result.Token);
        Assert.AreEqual("jwt-token", result.Token!.AccessToken);
        users.VerifyAll();
    }
}
