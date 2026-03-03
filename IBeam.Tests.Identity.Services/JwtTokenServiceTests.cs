using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tokens;
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

    private static JwtTokenService CreateSut(IAuthSessionStore sessions)
    {
        var options = new JwtOptions
        {
            Issuer = "ibeam.test",
            Audience = "ibeam.clients",
            SigningKey = "test-signing-key-with-enough-length-1234567890",
            AccessTokenMinutes = 60,
            PreTenantTokenMinutes = 10,
            RefreshTokenDays = 30
        };

        return new JwtTokenService(Options.Create(options), sessions);
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
