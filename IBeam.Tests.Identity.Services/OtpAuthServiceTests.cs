using IBeam.Identity.Exceptions;
using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Auth;
using IBeam.Identity.Services.Tokens;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;

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

    [TestMethod]
    public async Task CompleteOtpAsync_WithJwtTokenService_DoesNotDuplicateReservedClaims()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var tenantProvisioning = new Mock<ITenantProvisioningService>(MockBehavior.Strict);
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
            .ReturnsAsync(new List<TenantInfo>
            {
                new(
                    tenantId,
                    "Tenant A",
                    new List<string> { "Owner", "Admin" },
                    true,
                    new List<Guid> { Guid.NewGuid(), Guid.NewGuid() })
            });

        var jwt = new JwtTokenService(
            Options.Create(new JwtOptions
            {
                Issuer = "ibeam.test",
                Audience = "ibeam.clients",
                SigningKey = "test-signing-key-with-enough-length-1234567890",
                AccessTokenMinutes = 60,
                PreTenantTokenMinutes = 10,
                RefreshTokenDays = 30
            }),
            new InMemoryAuthSessionStore());

        var sut = new OtpAuthService(
            users.Object,
            tenants.Object,
            tenantProvisioning.Object,
            jwt,
            otpService.Object,
            otpChallenges.Object);

        var result = await sut.CompleteOtpAsync("challenge-1", "123456", "abram.cookson@outlook.com");

        Assert.IsNotNull(result.Token);
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(result.Token!.AccessToken);

        Assert.AreEqual(1, parsed.Claims.Count(c => c.Type == "sub"));
        Assert.AreEqual(1, parsed.Claims.Count(c => c.Type == "uid"));
        Assert.AreEqual(1, parsed.Claims.Count(c => c.Type == "tid"));
        Assert.AreEqual(1, parsed.Claims.Count(c => c.Type == "sid"));
        Assert.AreEqual(2, parsed.Claims.Count(c => c.Type == "role"));
        Assert.AreEqual(2, parsed.Claims.Count(c => c.Type == "rid"));
    }

    [TestMethod]
    public async Task CompleteOtpAsync_NewUser_EmitsAuthLifecycleEvents()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var tenantProvisioning = new Mock<ITenantProvisioningService>(MockBehavior.Strict);
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var otpChallenges = new Mock<IOtpChallengeStore>(MockBehavior.Strict);
        var publisher = new Mock<IAuthEventPublisher>(MockBehavior.Strict);
        var hook = new Mock<IAuthLifecycleHook>(MockBehavior.Strict);

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
            .ReturnsAsync((IdentityUser?)null);
        users.Setup(x => x.CreateAsync(It.IsAny<RegisterUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateUserResult.Success(new IdentityUser(userId, "abram.cookson@outlook.com", true)));

        tenants.SetupSequence(x => x.GetTenantsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInfo>())
            .ReturnsAsync(new List<TenantInfo> { new(tenantId, "Tenant A", new List<string> { "Owner", "Admin" }, true) });

        tenantProvisioning.Setup(x => x.CreateTenantForNewUserAsync(userId, "ABRAM.COOKSON@OUTLOOK.COM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantId);

        tokens.Setup(x => x.CreateAccessTokenAsync(
                userId,
                tenantId,
                It.IsAny<IReadOnlyList<ClaimItem>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResult("jwt-token", DateTimeOffset.UtcNow.AddMinutes(60), new List<ClaimItem>()));

        publisher.Setup(x => x.PublishAsync(It.IsAny<AuthUserCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(x => x.PublishAsync(It.IsAny<TenantCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(x => x.PublishAsync(It.IsAny<TenantUserLinkedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        hook.Setup(x => x.OnAuthUserCreatedAsync(It.IsAny<AuthUserCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        hook.Setup(x => x.OnTenantCreatedAsync(It.IsAny<TenantCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        hook.Setup(x => x.OnTenantUserLinkedAsync(It.IsAny<TenantUserLinkedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new OtpAuthService(
            users.Object,
            tenants.Object,
            tenantProvisioning.Object,
            tokens.Object,
            otpService.Object,
            otpChallenges.Object,
            publisher.Object,
            hook.Object,
            Options.Create(new AuthEventOptions()),
            NullLogger<OtpAuthService>.Instance);

        var result = await sut.CompleteOtpAsync("challenge-1", "123456", "abram.cookson@outlook.com");

        Assert.IsNotNull(result.Token);
        Assert.AreEqual("jwt-token", result.Token!.AccessToken);

        publisher.Verify(x => x.PublishAsync(It.IsAny<AuthUserCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(x => x.PublishAsync(It.IsAny<TenantCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(x => x.PublishAsync(It.IsAny<TenantUserLinkedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CompleteOtpAsync_PublishFailure_NonBlockingByDefault_ReturnsToken()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var tenantProvisioning = new Mock<ITenantProvisioningService>(MockBehavior.Strict);
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var otpChallenges = new Mock<IOtpChallengeStore>(MockBehavior.Strict);
        var publisher = new Mock<IAuthEventPublisher>(MockBehavior.Strict);

        otpService.Setup(x => x.VerifyAsync(It.IsAny<OtpVerifyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerifyResult(true, "vt", DateTimeOffset.UtcNow.AddMinutes(10)));

        otpChallenges.Setup(x => x.GetAsync("challenge-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpChallengeRecord(
                "challenge-1", "abram.cookson@outlook.com", SenderPurpose.LoginMfa, "hash",
                DateTimeOffset.UtcNow.AddMinutes(10), 0, null, true, "vt", DateTimeOffset.UtcNow.AddMinutes(10)));

        users.Setup(x => x.FindByEmailAsync("ABRAM.COOKSON@OUTLOOK.COM", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUser?)null);
        users.Setup(x => x.CreateAsync(It.IsAny<RegisterUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateUserResult.Success(new IdentityUser(userId, "abram.cookson@outlook.com", true)));

        tenants.SetupSequence(x => x.GetTenantsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInfo>())
            .ReturnsAsync(new List<TenantInfo> { new(tenantId, "Tenant A", new List<string> { "Owner" }, true) });

        tenantProvisioning.Setup(x => x.CreateTenantForNewUserAsync(userId, "ABRAM.COOKSON@OUTLOOK.COM", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantId);

        tokens.Setup(x => x.CreateAccessTokenAsync(userId, tenantId, It.IsAny<IReadOnlyList<ClaimItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResult("jwt-token", DateTimeOffset.UtcNow.AddMinutes(60), new List<ClaimItem>()));

        publisher.Setup(x => x.PublishAsync(It.IsAny<AuthUserCreatedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("publish failed"));
        publisher.Setup(x => x.PublishAsync(It.IsAny<TenantCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(x => x.PublishAsync(It.IsAny<TenantUserLinkedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new OtpAuthService(
            users.Object,
            tenants.Object,
            tenantProvisioning.Object,
            tokens.Object,
            otpService.Object,
            otpChallenges.Object,
            publisher.Object,
            new NoOpAuthLifecycleHook(),
            Options.Create(new AuthEventOptions { StrictPublishFailures = false }),
            NullLogger<OtpAuthService>.Instance);

        var result = await sut.CompleteOtpAsync("challenge-1", "123456", "abram.cookson@outlook.com");

        Assert.IsNotNull(result.Token);
        Assert.AreEqual("jwt-token", result.Token!.AccessToken);
    }

    [TestMethod]
    public async Task CompleteOtpAsync_PublishFailure_StrictMode_Throws()
    {
        var userId = Guid.NewGuid();

        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        var tenantProvisioning = new Mock<ITenantProvisioningService>(MockBehavior.Strict);
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        var otpService = new Mock<IOtpService>(MockBehavior.Strict);
        var otpChallenges = new Mock<IOtpChallengeStore>(MockBehavior.Strict);
        var publisher = new Mock<IAuthEventPublisher>(MockBehavior.Strict);

        otpService.Setup(x => x.VerifyAsync(It.IsAny<OtpVerifyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerifyResult(true, "vt", DateTimeOffset.UtcNow.AddMinutes(10)));

        otpChallenges.Setup(x => x.GetAsync("challenge-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpChallengeRecord(
                "challenge-1", "abram.cookson@outlook.com", SenderPurpose.LoginMfa, "hash",
                DateTimeOffset.UtcNow.AddMinutes(10), 0, null, true, "vt", DateTimeOffset.UtcNow.AddMinutes(10)));

        users.Setup(x => x.FindByEmailAsync("ABRAM.COOKSON@OUTLOOK.COM", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityUser?)null);
        users.Setup(x => x.CreateAsync(It.IsAny<RegisterUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateUserResult.Success(new IdentityUser(userId, "abram.cookson@outlook.com", true)));

        publisher.Setup(x => x.PublishAsync(It.IsAny<LoginAttemptedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(x => x.PublishAsync(It.IsAny<OtpVerifyRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(x => x.PublishAsync(It.IsAny<OtpVerifiedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(x => x.PublishAsync(It.IsAny<AuthUserCreateRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(x => x.PublishAsync(It.IsAny<AuthUserCreatedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("publish failed"));

        var sut = new OtpAuthService(
            users.Object,
            tenants.Object,
            tenantProvisioning.Object,
            tokens.Object,
            otpService.Object,
            otpChallenges.Object,
            publisher.Object,
            new NoOpAuthLifecycleHook(),
            Options.Create(new AuthEventOptions { StrictPublishFailures = true }),
            NullLogger<OtpAuthService>.Instance);

        await AssertThrowsAsync<InvalidOperationException>(() =>
            sut.CompleteOtpAsync("challenge-1", "123456", "abram.cookson@outlook.com"));
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

    private sealed class InMemoryAuthSessionStore : IAuthSessionStore
    {
        private readonly Dictionary<string, AuthSessionRecord> _byHash = new(StringComparer.OrdinalIgnoreCase);

        public Task SaveAsync(AuthSessionRecord record, CancellationToken ct = default)
        {
            _byHash[record.RefreshTokenHash] = record;
            return Task.CompletedTask;
        }

        public Task<AuthSessionRecord?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default)
        {
            _byHash.TryGetValue(refreshTokenHash, out var record);
            return Task.FromResult(record);
        }

        public Task DeleteByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default)
        {
            _byHash.Remove(refreshTokenHash);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuthSessionRecord>> GetByUserAsync(Guid userId, CancellationToken ct = default)
        {
            IReadOnlyList<AuthSessionRecord> sessions = _byHash.Values
                .Where(x => x.UserId == userId)
                .ToList();
            return Task.FromResult(sessions);
        }

        public Task<bool> RevokeBySessionIdAsync(Guid userId, string sessionId, CancellationToken ct = default)
        {
            var hit = _byHash.Values.FirstOrDefault(x => x.UserId == userId && x.SessionId == sessionId);
            if (hit is null)
                return Task.FromResult(false);

            _byHash[hit.RefreshTokenHash] = hit with { RevokedAt = DateTimeOffset.UtcNow };
            return Task.FromResult(true);
        }
    }

}
