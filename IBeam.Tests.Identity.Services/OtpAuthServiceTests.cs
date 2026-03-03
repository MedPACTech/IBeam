using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Auth;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OtpAuthServiceTests
{
    [TestMethod]
    public async Task StartOtpAsync_WithEmail_NormalizesAndCreatesLoginChallenge()
    {
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        otpService.Setup(x => x.CreateChallengeAsync(
                It.Is<OtpChallengeRequest>(r =>
                    r.Channel == SenderChannel.Email &&
                    r.Purpose == SenderPurpose.LoginMfa &&
                    r.Destination == "ABRAM.COOKSON@OUTLOOK.COM"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpChallengeResult("otp-1", DateTimeOffset.UtcNow.AddMinutes(10)));

        var sut = new OtpAuthService(
            Mock.Of<IIdentityUserStore>(),
            Mock.Of<ITenantMembershipStore>(),
            Mock.Of<ITenantProvisioningService>(),
            Mock.Of<ITokenService>(),
            otpService.Object,
            Mock.Of<IOtpChallengeStore>());

        var result = await sut.StartOtpAsync("  Abram.Cookson@Outlook.com ");

        Assert.AreEqual("otp-1", result.ChallengeId);
        otpService.VerifyAll();
    }

    [TestMethod]
    public async Task CompleteOtpAsync_WhenVerificationFails_ThrowsValidation()
    {
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        otpService.Setup(x => x.VerifyAsync(
                It.IsAny<OtpVerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerifyResult(false));

        var sut = new OtpAuthService(
            Mock.Of<IIdentityUserStore>(),
            Mock.Of<ITenantMembershipStore>(),
            Mock.Of<ITenantProvisioningService>(),
            Mock.Of<ITokenService>(),
            otpService.Object,
            Mock.Of<IOtpChallengeStore>());

        await AssertThrowsAsync<IdentityValidationException>(() =>
            sut.CompleteOtpAsync("challenge-1", "123456", "abram.cookson@outlook.com"));
    }

    [TestMethod]
    public async Task CompleteOtpAsync_WhenExistingUserSingleTenant_ReturnsToken()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var tenantProvisioning = new Mock<ITenantProvisioningService>(MockBehavior.Strict);
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var otpChallenges = new Mock<IOtpChallengeStore>(MockBehavior.Strict);

        otpService.Setup(x => x.VerifyAsync(
                It.IsAny<OtpVerifyRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerifyResult(true, "vt", DateTimeOffset.UtcNow.AddMinutes(10)));

        otpChallenges.Setup(x => x.GetAsync("challenge-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpChallengeRecord(
                ChallengeId: "challenge-1",
                Destination: "abram.cookson@outlook.com",
                Purpose: SenderPurpose.LoginMfa,
                CodeHash: "hash",
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
                AttemptCount: 0,
                TenantId: null,
                IsConsumed: true,
                VerificationToken: "vt",
                VerificationTokenExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10)));

        users.Setup(x => x.FindByEmailAsync("ABRAM.COOKSON@OUTLOOK.COM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUser(userId, "abram.cookson@outlook.com", true));

        tenants.Setup(x => x.GetTenantsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInfo> { new(tenantId, "Tenant A", new List<string> { "User" }, true) });

        tokens.Setup(x => x.CreateAccessTokenAsync(
                userId,
                tenantId,
                It.IsAny<IReadOnlyList<ClaimItem>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResult("jwt-token", DateTimeOffset.UtcNow.AddMinutes(60), new List<ClaimItem>()));

        var sut = new OtpAuthService(
            users.Object,
            tenants.Object,
            tenantProvisioning.Object,
            tokens.Object,
            otpService.Object,
            otpChallenges.Object);

        var result = await sut.CompleteOtpAsync("challenge-1", "123456", "abram.cookson@outlook.com");

        Assert.IsNotNull(result.Token);
        Assert.AreEqual("jwt-token", result.Token!.AccessToken);
        Assert.IsFalse(result.RequiresTenantSelection);
        Assert.IsFalse(result.IsNewUser);
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected exception {typeof(TException).Name} was not thrown.");
            throw new InvalidOperationException("Unreachable");
        }
        catch (TException ex)
        {
            return ex;
        }
    }
}
