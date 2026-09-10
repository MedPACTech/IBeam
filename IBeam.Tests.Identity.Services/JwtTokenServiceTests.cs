using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tokens;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class JwtTokenServiceTests
{
    [TestMethod]
    public async Task CreateAccessTokenAsync_SavesSessionAndReturnsRefreshToken()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        AuthSessionRecord? saved = null;
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuthSessionRecord, CancellationToken>((record, _) => saved = record)
            .Returns(Task.CompletedTask);

        var sut = CreateSut(sessions.Object);
        var claims = new List<ClaimItem> { new("email", "abram.cookson@outlook.com") };

        var token = await sut.CreateAccessTokenAsync(userId, tenantId, claims);

        Assert.IsFalse(string.IsNullOrWhiteSpace(token.AccessToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(token.RefreshToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(token.SessionId));
        Assert.IsNotNull(saved);
        Assert.AreEqual(userId, saved!.UserId);
        Assert.AreEqual(tenantId, saved.TenantId);
        Assert.AreEqual(token.SessionId, saved.SessionId);
    }

    [TestMethod]
    public async Task CreateAccessTokenAsync_NormalizesReservedClaimsWithoutRemovingMultiValueRoles()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(sessions.Object);
        var claims = new List<ClaimItem>
        {
            new("sub", "stale-sub"),
            new("uid", "stale-uid"),
            new("tid", Guid.NewGuid().ToString("D")),
            new("sid", "stale-session"),
            new("role", "Owner"),
            new("role", "Admin"),
            new("email", "abram.cookson@outlook.com")
        };

        var token = await sut.CreateAccessTokenAsync(userId, tenantId, claims);

        Assert.AreEqual(1, token.Claims.Count(c => string.Equals(c.Type, "sub", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(1, token.Claims.Count(c => string.Equals(c.Type, "uid", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(1, token.Claims.Count(c => string.Equals(c.Type, "tid", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(1, token.Claims.Count(c => string.Equals(c.Type, "sid", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(2, token.Claims.Count(c => string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase)));

        Assert.AreEqual(userId.ToString("D"), token.Claims.Single(c => c.Type == "sub").Value);
        Assert.AreEqual(userId.ToString("D"), token.Claims.Single(c => c.Type == "uid").Value);
        Assert.AreEqual(tenantId.ToString("D"), token.Claims.Single(c => c.Type == "tid").Value);
        Assert.AreEqual(token.SessionId, token.Claims.Single(c => c.Type == "sid").Value);
    }

    [TestMethod]
    public async Task CreateAccessTokenAsync_AddsClaimsFromEnrichers()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        AuthSessionRecord? saved = null;
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuthSessionRecord, CancellationToken>((record, _) => saved = record)
            .Returns(Task.CompletedTask);

        var sut = CreateSut(
            sessions.Object,
            [new TestClaimsEnricher(new ClaimItem("resource_access", "{\"grants\":[]}", "json"))]);

        var token = await sut.CreateAccessTokenAsync(userId, tenantId, []);

        Assert.AreEqual("{\"grants\":[]}", token.Claims.Single(x => x.Type == "resource_access").Value);
        Assert.IsNotNull(saved);
        var persisted = JsonSerializer.Deserialize<List<ClaimItem>>(saved!.ClaimsJson) ?? [];
        Assert.AreEqual("{\"grants\":[]}", persisted.Single(x => x.Type == "resource_access").Value);
    }

    [TestMethod]
    public async Task RefreshAccessTokenAsync_RotatesRefreshTokenAndPreservesSessionId()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        const string sessionId = "session-1";
        const string oldRefresh = "refresh-1";

        var oldHash = HashRefreshToken(oldRefresh);
        var existingClaims = new List<ClaimItem>
        {
            new("sub", userId.ToString("D")),
            new("uid", userId.ToString("D")),
            new("tid", tenantId.ToString("D")),
            new("sid", sessionId)
        };

        var existing = new AuthSessionRecord(
            RefreshTokenHash: oldHash,
            SessionId: sessionId,
            UserId: userId,
            TenantId: tenantId,
            ClaimsJson: JsonSerializer.Serialize(existingClaims),
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            LastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(1));

        AuthSessionRecord? rotated = null;
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.GetByRefreshTokenHashAsync(oldHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        sessions.Setup(x => x.DeleteByRefreshTokenHashAsync(oldHash, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuthSessionRecord, CancellationToken>((record, _) => rotated = record)
            .Returns(Task.CompletedTask);

        var sut = CreateSut(sessions.Object);

        var refreshed = await sut.RefreshAccessTokenAsync(oldRefresh);

        Assert.IsNotNull(rotated);
        Assert.AreEqual(sessionId, refreshed.SessionId);
        Assert.IsFalse(string.Equals(oldRefresh, refreshed.RefreshToken, StringComparison.Ordinal));
        Assert.AreEqual(sessionId, rotated!.SessionId);
        Assert.AreNotEqual(oldHash, rotated.RefreshTokenHash);
    }

    [TestMethod]
    public async Task RefreshAccessTokenAsync_ReplacesClaimsFromEnrichers()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        const string sessionId = "session-1";
        const string oldRefresh = "refresh-1";

        var oldHash = HashRefreshToken(oldRefresh);
        var existingClaims = new List<ClaimItem>
        {
            new("sub", userId.ToString("D")),
            new("uid", userId.ToString("D")),
            new("tid", tenantId.ToString("D")),
            new("sid", sessionId),
            new("resource_access", "{\"grants\":[{\"resourceId\":\"stale\"}]}", "json")
        };

        var existing = new AuthSessionRecord(
            RefreshTokenHash: oldHash,
            SessionId: sessionId,
            UserId: userId,
            TenantId: tenantId,
            ClaimsJson: JsonSerializer.Serialize(existingClaims),
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            LastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(1));

        AuthSessionRecord? rotated = null;
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.GetByRefreshTokenHashAsync(oldHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        sessions.Setup(x => x.DeleteByRefreshTokenHashAsync(oldHash, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuthSessionRecord, CancellationToken>((record, _) => rotated = record)
            .Returns(Task.CompletedTask);

        var sut = CreateSut(
            sessions.Object,
            [new TestClaimsEnricher(new ClaimItem("resource_access", "{\"grants\":[]}", "json"))]);

        var refreshed = await sut.RefreshAccessTokenAsync(oldRefresh);

        Assert.AreEqual("{\"grants\":[]}", refreshed.Claims.Single(x => x.Type == "resource_access").Value);
        Assert.IsNotNull(rotated);
        var persisted = JsonSerializer.Deserialize<List<ClaimItem>>(rotated!.ClaimsJson) ?? [];
        Assert.AreEqual("{\"grants\":[]}", persisted.Single(x => x.Type == "resource_access").Value);
    }

    [TestMethod]
    public async Task RefreshAccessTokenAsync_WhenSessionMissing_ThrowsUnauthorized()
    {
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthSessionRecord?)null);

        var sut = CreateSut(sessions.Object);

        await AssertThrowsAsync<IdentityUnauthorizedException>(() =>
            sut.RefreshAccessTokenAsync("unknown-refresh-token"));
    }

    [TestMethod]
    public async Task GetUserSessionsAsync_ReturnsSessionsOrderedByLastSeenDesc()
    {
        var userId = Guid.NewGuid();
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.GetByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthSessionRecord>
            {
                new("h1", "s1", userId, Guid.NewGuid(), "[]", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddMinutes(-20), DateTimeOffset.UtcNow.AddDays(1)),
                new("h2", "s2", userId, Guid.NewGuid(), "[]", DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1)),
                new("h3", "s3", userId, Guid.NewGuid(), "[]", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMinutes(-60), DateTimeOffset.UtcNow.AddDays(1))
            });

        var sut = CreateSut(sessions.Object);

        var result = await sut.GetUserSessionsAsync(userId);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("s2", result[0].SessionId);
        Assert.AreEqual("s1", result[1].SessionId);
        Assert.AreEqual("s3", result[2].SessionId);
    }

    [TestMethod]
    public async Task RevokeSessionAsync_WithEmptySessionId_ThrowsValidation()
    {
        var sut = CreateSut(Mock.Of<IAuthSessionStore>(MockBehavior.Strict));
        var userId = Guid.NewGuid();

        await AssertThrowsAsync<IdentityValidationException>(() =>
            sut.RevokeSessionAsync(userId, " "));
    }

    [TestMethod]
    public async Task RefreshAccessTokenAsync_WhenSessionRevoked_ThrowsUnauthorized()
    {
        const string refreshToken = "refresh-1";
        var hash = HashRefreshToken(refreshToken);
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.GetByRefreshTokenHashAsync(hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthSessionRecord(
                RefreshTokenHash: hash,
                SessionId: "session-1",
                UserId: userId,
                TenantId: tenantId,
                ClaimsJson: "[]",
                CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
                LastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-10),
                RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(1),
                RevokedAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        var sut = CreateSut(sessions.Object);

        await AssertThrowsAsync<IdentityUnauthorizedException>(() =>
            sut.RefreshAccessTokenAsync(refreshToken));
    }

    [TestMethod]
    public async Task CreateAccessTokenAsync_WithInactivityWindow_UsesMinutesNotDays()
    {
        AuthSessionRecord? saved = null;
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuthSessionRecord, CancellationToken>((record, _) => saved = record)
            .Returns(Task.CompletedTask);

        var sut = CreateSut(sessions.Object, options: SlidingOptions(inactivityMinutes: 120));

        await sut.CreateAccessTokenAsync(Guid.NewGuid(), Guid.NewGuid(), []);

        Assert.IsNotNull(saved);
        var window = saved!.RefreshTokenExpiresAt - DateTimeOffset.UtcNow;
        Assert.IsTrue(window > TimeSpan.FromMinutes(118) && window <= TimeSpan.FromMinutes(120),
            $"Expected ~120 minute window, got {window}.");
    }

    [TestMethod]
    public async Task RefreshAccessTokenAsync_WithInactivityWindow_SlidesExpiryFromNow()
    {
        const string oldRefresh = "refresh-1";
        var oldHash = HashRefreshToken(oldRefresh);
        var existing = SessionRecord(oldHash, createdAt: DateTimeOffset.UtcNow.AddHours(-6),
            refreshExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30));

        AuthSessionRecord? rotated = null;
        var sessions = RotatingStore(oldHash, existing, record => rotated = record);

        var sut = CreateSut(sessions.Object, options: SlidingOptions(inactivityMinutes: 120));

        await sut.RefreshAccessTokenAsync(oldRefresh);

        Assert.IsNotNull(rotated);
        var window = rotated!.RefreshTokenExpiresAt - DateTimeOffset.UtcNow;
        Assert.IsTrue(window > TimeSpan.FromMinutes(118) && window <= TimeSpan.FromMinutes(120),
            $"Expected the window to slide to ~120 minutes from now, got {window}.");
    }

    [TestMethod]
    public async Task RefreshAccessTokenAsync_CapsSlidingWindowAtAbsoluteLifetime()
    {
        const string oldRefresh = "refresh-1";
        var oldHash = HashRefreshToken(oldRefresh);
        var createdAt = DateTimeOffset.UtcNow.AddHours(-12);
        var existing = SessionRecord(oldHash, createdAt, refreshExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30));

        AuthSessionRecord? rotated = null;
        var sessions = RotatingStore(oldHash, existing, record => rotated = record);

        var sut = CreateSut(sessions.Object,
            options: SlidingOptions(inactivityMinutes: 24 * 60, absoluteLifetimeDays: 1));

        await sut.RefreshAccessTokenAsync(oldRefresh);

        Assert.IsNotNull(rotated);
        Assert.AreEqual(createdAt.AddDays(1), rotated!.RefreshTokenExpiresAt);
    }

    [TestMethod]
    public async Task RefreshAccessTokenAsync_WhenAbsoluteLifetimeExceeded_ThrowsUnauthorized()
    {
        const string oldRefresh = "refresh-1";
        var oldHash = HashRefreshToken(oldRefresh);
        var existing = SessionRecord(oldHash, createdAt: DateTimeOffset.UtcNow.AddDays(-2),
            refreshExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30));

        var sessions = RotatingStore(oldHash, existing, _ => { });

        var sut = CreateSut(sessions.Object,
            options: SlidingOptions(inactivityMinutes: 120, absoluteLifetimeDays: 1));

        await AssertThrowsAsync<IdentityUnauthorizedException>(() =>
            sut.RefreshAccessTokenAsync(oldRefresh));
    }

    [TestMethod]
    public async Task CreateAccessTokenAsync_WhenRemembered_UsesRememberedRefreshTokenDays()
    {
        AuthSessionRecord? saved = null;
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuthSessionRecord, CancellationToken>((record, _) => saved = record)
            .Returns(Task.CompletedTask);

        var sut = CreateSut(sessions.Object, options: RememberedOptions(refreshTokenDays: 7, rememberedRefreshTokenDays: 60));

        await sut.CreateAccessTokenAsync(Guid.NewGuid(), Guid.NewGuid(), [], rememberDevice: true);

        Assert.IsNotNull(saved);
        Assert.IsTrue(saved!.Remembered);
        var window = saved.RefreshTokenExpiresAt - DateTimeOffset.UtcNow;
        Assert.IsTrue(window > TimeSpan.FromDays(59) && window <= TimeSpan.FromDays(60),
            $"Expected ~60 day remembered window, got {window}.");
    }

    [TestMethod]
    public async Task CreateAccessTokenAsync_WhenNotRemembered_UsesNormalRefreshTokenDays()
    {
        AuthSessionRecord? saved = null;
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuthSessionRecord, CancellationToken>((record, _) => saved = record)
            .Returns(Task.CompletedTask);

        var sut = CreateSut(sessions.Object, options: RememberedOptions(refreshTokenDays: 7, rememberedRefreshTokenDays: 60));

        await sut.CreateAccessTokenAsync(Guid.NewGuid(), Guid.NewGuid(), [], rememberDevice: false);

        Assert.IsNotNull(saved);
        Assert.IsFalse(saved!.Remembered);
        var window = saved.RefreshTokenExpiresAt - DateTimeOffset.UtcNow;
        Assert.IsTrue(window > TimeSpan.FromDays(6) && window <= TimeSpan.FromDays(7),
            $"Expected ~7 day normal window, got {window}.");
    }

    [TestMethod]
    public async Task CreateAccessTokenAsync_WhenRememberedButNoRememberedDaysConfigured_FallsBackToRefreshTokenDays()
    {
        AuthSessionRecord? saved = null;
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuthSessionRecord, CancellationToken>((record, _) => saved = record)
            .Returns(Task.CompletedTask);

        // No RememberedRefreshTokenDays set - unset means "remembered gets the same lifetime as everyone else".
        var sut = CreateSut(sessions.Object);

        await sut.CreateAccessTokenAsync(Guid.NewGuid(), Guid.NewGuid(), [], rememberDevice: true);

        Assert.IsNotNull(saved);
        Assert.IsTrue(saved!.Remembered);
        var window = saved.RefreshTokenExpiresAt - DateTimeOffset.UtcNow;
        Assert.IsTrue(window > TimeSpan.FromDays(29) && window <= TimeSpan.FromDays(30),
            $"Expected the default 30 day RefreshTokenDays window, got {window}.");
    }

    [TestMethod]
    public async Task RefreshAccessTokenAsync_PreservesRememberedAcrossRotation()
    {
        const string oldRefresh = "refresh-1";
        var oldHash = HashRefreshToken(oldRefresh);
        var existing = SessionRecord(oldHash, createdAt: DateTimeOffset.UtcNow.AddDays(-1),
            refreshExpiresAt: DateTimeOffset.UtcNow.AddDays(1)) with { Remembered = true };

        AuthSessionRecord? rotated = null;
        var sessions = RotatingStore(oldHash, existing, record => rotated = record);

        var sut = CreateSut(sessions.Object, options: RememberedOptions(refreshTokenDays: 7, rememberedRefreshTokenDays: 60));

        await sut.RefreshAccessTokenAsync(oldRefresh);

        Assert.IsNotNull(rotated);
        Assert.IsTrue(rotated!.Remembered, "A remembered session's rotation must not silently downgrade to the normal window.");
        var window = rotated.RefreshTokenExpiresAt - DateTimeOffset.UtcNow;
        Assert.IsTrue(window > TimeSpan.FromDays(59) && window <= TimeSpan.FromDays(60),
            $"Expected the remembered 60 day window to carry through rotation, got {window}.");
    }

    [TestMethod]
    public async Task RefreshAccessTokenAsync_WhenNotRemembered_KeepsNormalWindowOnRotation()
    {
        const string oldRefresh = "refresh-1";
        var oldHash = HashRefreshToken(oldRefresh);
        var existing = SessionRecord(oldHash, createdAt: DateTimeOffset.UtcNow.AddDays(-1),
            refreshExpiresAt: DateTimeOffset.UtcNow.AddDays(1)); // Remembered defaults to false

        AuthSessionRecord? rotated = null;
        var sessions = RotatingStore(oldHash, existing, record => rotated = record);

        var sut = CreateSut(sessions.Object, options: RememberedOptions(refreshTokenDays: 7, rememberedRefreshTokenDays: 60));

        await sut.RefreshAccessTokenAsync(oldRefresh);

        Assert.IsNotNull(rotated);
        Assert.IsFalse(rotated!.Remembered);
        var window = rotated.RefreshTokenExpiresAt - DateTimeOffset.UtcNow;
        Assert.IsTrue(window > TimeSpan.FromDays(6) && window <= TimeSpan.FromDays(7),
            $"Expected the normal 7 day window, got {window}.");
    }

    [TestMethod]
    public async Task RefreshAccessTokenAsync_WhenRemembered_CapsAtRememberedAbsoluteLifetime()
    {
        const string oldRefresh = "refresh-1";
        var oldHash = HashRefreshToken(oldRefresh);
        var createdAt = DateTimeOffset.UtcNow.AddDays(-89);
        var existing = SessionRecord(oldHash, createdAt, refreshExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30))
            with { Remembered = true };

        AuthSessionRecord? rotated = null;
        var sessions = RotatingStore(oldHash, existing, record => rotated = record);

        var sut = CreateSut(sessions.Object, options: RememberedOptions(
            refreshTokenDays: 7,
            rememberedRefreshTokenDays: 60,
            rememberedAbsoluteLifetimeDays: 90));

        await sut.RefreshAccessTokenAsync(oldRefresh);

        Assert.IsNotNull(rotated);
        Assert.AreEqual(createdAt.AddDays(90), rotated!.RefreshTokenExpiresAt);
    }

    [TestMethod]
    public async Task GetUserSessionsAsync_SurfacesRememberedFlag()
    {
        var userId = Guid.NewGuid();
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.GetByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuthSessionRecord>
            {
                new("h1", "s1", userId, Guid.NewGuid(), "[]", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(60), Remembered: true),
                new("h2", "s2", userId, Guid.NewGuid(), "[]", DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddDays(7), Remembered: false)
            });

        var sut = CreateSut(sessions.Object);

        var result = await sut.GetUserSessionsAsync(userId);

        Assert.IsTrue(result.Single(s => s.SessionId == "s1").Remembered);
        Assert.IsFalse(result.Single(s => s.SessionId == "s2").Remembered);
    }

    [TestMethod]
    public void Constructor_WhenInactivityWindowShorterThanAccessToken_Throws()
    {
        Assert.ThrowsExactly<IdentityValidationException>(() =>
            CreateSut(Mock.Of<IAuthSessionStore>(), options: new JwtOptions
            {
                Issuer = "ibeam.test",
                Audience = "ibeam.clients",
                SigningKey = "test-signing-key-with-enough-length-1234567890",
                AccessTokenMinutes = 60,
                SessionInactivityMinutes = 30
            }));
    }

    private static JwtOptions SlidingOptions(int inactivityMinutes, int? absoluteLifetimeDays = null) => new()
    {
        Issuer = "ibeam.test",
        Audience = "ibeam.clients",
        SigningKey = "test-signing-key-with-enough-length-1234567890",
        AccessTokenMinutes = 60,
        PreTenantTokenMinutes = 10,
        RefreshTokenDays = 30,
        SessionInactivityMinutes = inactivityMinutes,
        SessionAbsoluteLifetimeDays = absoluteLifetimeDays
    };

    private static JwtOptions RememberedOptions(
        int refreshTokenDays,
        int? rememberedRefreshTokenDays = null,
        int? rememberedAbsoluteLifetimeDays = null) => new()
    {
        Issuer = "ibeam.test",
        Audience = "ibeam.clients",
        SigningKey = "test-signing-key-with-enough-length-1234567890",
        AccessTokenMinutes = 60,
        PreTenantTokenMinutes = 10,
        RefreshTokenDays = refreshTokenDays,
        RememberedRefreshTokenDays = rememberedRefreshTokenDays,
        RememberedSessionAbsoluteLifetimeDays = rememberedAbsoluteLifetimeDays
    };

    private static AuthSessionRecord SessionRecord(string refreshTokenHash, DateTimeOffset createdAt, DateTimeOffset refreshExpiresAt)
    {
        var userId = Guid.NewGuid();
        return new AuthSessionRecord(
            RefreshTokenHash: refreshTokenHash,
            SessionId: "session-1",
            UserId: userId,
            TenantId: Guid.NewGuid(),
            ClaimsJson: "[]",
            CreatedAt: createdAt,
            LastSeenAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            RefreshTokenExpiresAt: refreshExpiresAt);
    }

    private static Mock<IAuthSessionStore> RotatingStore(string oldHash, AuthSessionRecord existing, Action<AuthSessionRecord> onSave)
    {
        var sessions = new Mock<IAuthSessionStore>(MockBehavior.Strict);
        sessions.Setup(x => x.GetByRefreshTokenHashAsync(oldHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        sessions.Setup(x => x.DeleteByRefreshTokenHashAsync(oldHash, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AuthSessionRecord, CancellationToken>((record, _) => onSave(record))
            .Returns(Task.CompletedTask);
        return sessions;
    }

    private static JwtTokenService CreateSut(
        IAuthSessionStore sessions,
        IEnumerable<IClaimsEnricher>? claimsEnrichers = null,
        JwtOptions? options = null)
    {
        options ??= new JwtOptions
        {
            Issuer = "ibeam.test",
            Audience = "ibeam.clients",
            SigningKey = "test-signing-key-with-enough-length-1234567890",
            AccessTokenMinutes = 60,
            PreTenantTokenMinutes = 10,
            RefreshTokenDays = 30
        };

        return new JwtTokenService(
            Options.Create(options),
            sessions,
            new NoOpAuthEventPublisher(),
            new NoOpAuthLifecycleHook(),
            Options.Create(new AuthEventOptions()),
            NullLogger<JwtTokenService>.Instance,
            claimsEnrichers ?? []);
    }

    private sealed class TestClaimsEnricher : IClaimsEnricher
    {
        private readonly ClaimItem _claim;

        public TestClaimsEnricher(ClaimItem claim)
        {
            _claim = claim;
        }

        public Task<IReadOnlyList<ClaimItem>> EnrichAsync(
            ClaimsEnrichmentContext context,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ClaimItem>>([_claim]);
    }

    private static string HashRefreshToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
