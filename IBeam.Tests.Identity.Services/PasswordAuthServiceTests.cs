using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Services.Auth;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class PasswordAuthServiceTests
{
    [TestMethod]
    public async Task PasswordLoginAsync_WhenTwoFactorEnabled_ReturnsTwoFactorChallenge()
    {
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var tenantProvisioning = new Mock<ITenantProvisioningService>(MockBehavior.Strict);
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        var otp = new Mock<IOtpService>(MockBehavior.Strict);
        var otpChallenges = new Mock<IOtpChallengeStore>(MockBehavior.Strict);
        var sender = new Mock<IIdentityCommunicationSender>(MockBehavior.Strict);

        var user = new IdentityUser(
            UserId: Guid.NewGuid(),
            Email: "abram.cookson@outlook.com",
            EmailConfirmed: true,
            PhoneNumber: null,
            PhoneConfirmed: false,
            DisplayName: "Abram",
            TwoFactorEnabled: true,
            PreferredTwoFactorMethod: "email");

        users.Setup(x => x.FindByEmailAsync("abram.cookson@outlook.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        users.Setup(x => x.ValidatePasswordAsync("abram.cookson@outlook.com", "Pass123!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        otp.Setup(x => x.CreateChallengeAsync(
                It.Is<OtpChallengeRequest>(r =>
                    r.Channel == SenderChannel.Email &&
                    r.Purpose == SenderPurpose.LoginMfa &&
                    r.Destination == "abram.cookson@outlook.com"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpChallengeResult("ch-123", DateTimeOffset.UtcNow.AddMinutes(10)));

        var sut = new PasswordAuthService(
            users.Object,
            tenants.Object,
            tenantProvisioning.Object,
            tokens.Object,
            otp.Object,
            otpChallenges.Object,
            sender.Object);

        var result = await sut.PasswordLoginAsync(new PasswordLoginRequest("abram.cookson@outlook.com", "Pass123!"));

        Assert.IsTrue(result.RequiresTwoFactor);
        Assert.AreEqual("ch-123", result.TwoFactorChallengeId);
        Assert.AreEqual("email", result.TwoFactorMethod);
        Assert.IsNull(result.Token);

        users.VerifyAll();
        otp.VerifyAll();
    }

    [TestMethod]
    public async Task PasswordLoginAsync_WhenPasswordInvalid_ThrowsUnauthorized()
    {
        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var sut = new PasswordAuthService(
            users.Object,
            Mock.Of<ITenantMembershipStore>(),
            Mock.Of<ITenantProvisioningService>(),
            Mock.Of<ITokenService>(),
            Mock.Of<IOtpService>(),
            Mock.Of<IOtpChallengeStore>(),
            Mock.Of<IIdentityCommunicationSender>());

        users.Setup(x => x.FindByEmailAsync("abram.cookson@outlook.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUser(Guid.NewGuid(), "abram.cookson@outlook.com", true));
        users.Setup(x => x.ValidatePasswordAsync("abram.cookson@outlook.com", "bad-pass", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await AssertThrowsAsync<IdentityUnauthorizedException>(() =>
            sut.PasswordLoginAsync(new PasswordLoginRequest("abram.cookson@outlook.com", "bad-pass")));
    }

    [TestMethod]
    public async Task StartEmailPasswordRegistrationAsync_SavesChallengeAndSendsEmail()
    {
        var users = new Mock<IIdentityUserStore>(MockBehavior.Loose);
        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Loose);
        var tenantProvisioning = new Mock<ITenantProvisioningService>(MockBehavior.Loose);
        var tokens = new Mock<ITokenService>(MockBehavior.Loose);
        var otp = new Mock<IOtpService>(MockBehavior.Loose);
        var otpChallenges = new Mock<IOtpChallengeStore>(MockBehavior.Strict);
        var sender = new Mock<IIdentityCommunicationSender>(MockBehavior.Strict);

        otpChallenges.Setup(x => x.SaveAsync(
                It.Is<OtpChallengeRecord>(r =>
                    r.Purpose == SenderPurpose.UserRegistration &&
                    r.Destination == "abram.cookson@outlook.com" &&
                    r.IsConsumed),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        sender.Setup(x => x.SendAsync(
                It.Is<IdentitySenderMessage>(m =>
                    m.Channel == SenderChannel.Email &&
                    m.Destination == "abram.cookson@outlook.com" &&
                    m.Purpose == SenderPurpose.UserRegistration &&
                    m.Metadata != null &&
                    m.Metadata.ContainsKey("Link")),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new PasswordAuthService(
            users.Object,
            tenants.Object,
            tenantProvisioning.Object,
            tokens.Object,
            otp.Object,
            otpChallenges.Object,
            sender.Object);

        var response = await sut.StartEmailPasswordRegistrationAsync("abram.cookson@outlook.com", "Abram", "https://localhost:3000/reset-password");

        Assert.IsTrue(response.Accepted);
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.ChallengeId));
        otpChallenges.VerifyAll();
        sender.VerifyAll();
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
