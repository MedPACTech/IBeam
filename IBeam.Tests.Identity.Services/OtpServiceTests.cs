using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Abstractions.Options;
using IBeam.Identity.Services.Otp;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OtpServiceTests
{
    [TestMethod]
    public async Task CreateChallengeAsync_SavesChallengeAndSendsCode()
    {
        OtpChallengeRecord? saved = null;

        var store = new Mock<IOtpChallengeStore>(MockBehavior.Strict);
        store.Setup(x => x.SaveAsync(It.IsAny<OtpChallengeRecord>(), It.IsAny<CancellationToken>()))
            .Callback<OtpChallengeRecord, CancellationToken>((record, _) => saved = record)
            .Returns(Task.CompletedTask);

        var sender = new Mock<IIdentityCommunicationSender>(MockBehavior.Strict);
        sender.Setup(x => x.SendAsync(
                It.Is<IdentitySenderMessage>(m =>
                    m.Channel == SenderChannel.Email &&
                    m.Destination == "abram.cookson@outlook.com" &&
                    m.Purpose == SenderPurpose.LoginMfa &&
                    !string.IsNullOrWhiteSpace(m.Code) &&
                    m.Code!.Length == 6),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(store.Object, sender.Object);

        var result = await sut.CreateChallengeAsync(new OtpChallengeRequest(
            SenderChannel.Email,
            "  abram.cookson@outlook.com ",
            SenderPurpose.LoginMfa,
            null));

        Assert.IsNotNull(saved);
        Assert.AreEqual("abram.cookson@outlook.com", saved!.Destination);
        Assert.IsFalse(saved.IsConsumed);
        Assert.AreEqual(0, saved.AttemptCount);
        Assert.AreEqual(saved.ChallengeId, result.ChallengeId);
    }

    [TestMethod]
    public async Task VerifyAsync_WhenCodeIsValid_MarksConsumedAndReturnsToken()
    {
        var opts = CreateOptions();
        var code = "123456";

        var record = new OtpChallengeRecord(
            ChallengeId: "challenge-1",
            Destination: "abram.cookson@outlook.com",
            Purpose: SenderPurpose.LoginMfa,
            CodeHash: HashCode(code, opts.HashSalt),
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
            AttemptCount: 0,
            TenantId: null,
            IsConsumed: false,
            VerificationToken: null,
            VerificationTokenExpiresAt: null);

        var store = new Mock<IOtpChallengeStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync("challenge-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        store.Setup(x => x.MarkConsumedAsync(
                "challenge-1",
                It.Is<string>(token => !string.IsNullOrWhiteSpace(token)),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sender = new Mock<IIdentityCommunicationSender>(MockBehavior.Strict);
        var sut = CreateSut(store.Object, sender.Object, opts);

        var result = await sut.VerifyAsync(new OtpVerifyRequest("challenge-1", code));

        Assert.IsTrue(result.Success);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.VerificationToken));
        store.VerifyAll();
    }

    [TestMethod]
    public async Task VerifyAsync_WhenCodeIsInvalid_IncrementsAttemptAndReturnsFalse()
    {
        var opts = CreateOptions();

        var record = new OtpChallengeRecord(
            ChallengeId: "challenge-1",
            Destination: "abram.cookson@outlook.com",
            Purpose: SenderPurpose.LoginMfa,
            CodeHash: HashCode("999999", opts.HashSalt),
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
            AttemptCount: 0,
            TenantId: null,
            IsConsumed: false,
            VerificationToken: null,
            VerificationTokenExpiresAt: null);

        var store = new Mock<IOtpChallengeStore>(MockBehavior.Strict);
        store.Setup(x => x.GetAsync("challenge-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        store.Setup(x => x.IncrementAttemptAsync("challenge-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sender = new Mock<IIdentityCommunicationSender>(MockBehavior.Strict);
        var sut = CreateSut(store.Object, sender.Object, opts);

        var result = await sut.VerifyAsync(new OtpVerifyRequest("challenge-1", "123456"));

        Assert.IsFalse(result.Success);
        Assert.IsNull(result.VerificationToken);
        store.VerifyAll();
    }

    private static OtpService CreateSut(IOtpChallengeStore store, IIdentityCommunicationSender sender, OtpOptions? options = null)
        => new(store, sender, OptionsMonitorOf(options ?? CreateOptions()));

    private static OtpOptions CreateOptions() => new()
    {
        CodeLength = 6,
        ExpirationMinutes = 5,
        MaxAttempts = 5,
        VerificationTokenMinutes = 10,
        HashSalt = "test-salt",
        VerificationTokenSecret = "test-secret"
    };

    private static IOptionsMonitor<OtpOptions> OptionsMonitorOf(OtpOptions value)
    {
        var monitor = new Mock<IOptionsMonitor<OtpOptions>>(MockBehavior.Strict);
        monitor.Setup(x => x.CurrentValue).Returns(value);
        return monitor.Object;
    }

    private static string HashCode(string code, string salt)
    {
        var bytes = Encoding.UTF8.GetBytes($"{salt}:{code}");
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
