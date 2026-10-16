using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management;
using IBeam.Management.Identity.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Tests.Management.Identity;

[TestClass]
[TestCategory("StorageIntegration")]
public sealed class ManagementIdentityAuthenticationTests
{
    [TestMethod]
    public async Task PasswordMfaRefreshAndLogoutUseRealIdentityServicesAndPersistedSessions()
    {
        await using var fixture = await ManagementIdentityHttpFixture.CreateAsync();
        using var anonymous = await fixture.GetIdentityAsync();
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var wrongPassword = await fixture.Client.PostAsJsonAsync("/api/auth/password-login",
            new { email = ManagementIdentityHttpFixture.Email, password = "not-the-fixture-password" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        var issued = await fixture.LoginAsync();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);
        Assert.IsTrue(jwt.Claims.Any(c => c.Type == "amr" && c.Value == "mfa"));
        Assert.AreEqual(fixture.Audience, jwt.Audiences.Single());
        using var me = await fixture.GetIdentityAsync(issued.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, me.StatusCode);
        using var refresh = await fixture.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = issued.RefreshToken });
        Assert.AreEqual(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = await ManagementIdentityHttpFixture.ReadDataAsync<TokenResult>(refresh);
        Assert.AreNotEqual(issued.RefreshToken, rotated.RefreshToken);
        using var replay = await fixture.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = issued.RefreshToken });
        Assert.AreEqual(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        using var logout = await fixture.Client.SendAsync(logoutRequest);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
        using var revoked = await fixture.GetIdentityAsync(rotated.AccessToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var revokedRefresh = await fixture.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rotated.RefreshToken });
        Assert.AreEqual(HttpStatusCode.Unauthorized, revokedRefresh.StatusCode);

        Assert.Throws<SecurityTokenInvalidAudienceException>(() => new JwtSecurityTokenHandler().ValidateToken(issued.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = fixture.Issuer, ValidateAudience = true, ValidAudience = "customer-api",
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(fixture.SigningKey))
            }, out _));
    }

    [TestMethod]
    [DataRow("membership")]
    [DataRow("organization")]
    [DataRow("role")]
    [DataRow("role-deleted")]
    [DataRow("mfa")]
    [DataRow("record-mfa")]
    [DataRow("session-expiry")]
    [DataRow("session-inactivity")]
    [DataRow("session-absolute")]
    [DataRow("session-revoked")]
    public async Task CurrentStateChangesImmediatelyRejectAccessAndRefresh(string change)
    {
        await using var fixture = await ManagementIdentityHttpFixture.CreateAsync();
        var issued = await fixture.LoginAsync();
        await fixture.WithScopeAsync(async services =>
        {
            if (change == "membership")
                await services.GetRequiredService<ITenantMembershipStore>().DisableTenantMembershipAsync(fixture.OrganizationId, fixture.UserId);
            if (change == "organization")
                await services.GetRequiredService<IIdentityTenantStore>().SetStatusAsync(fixture.OrganizationId, IdentityTenantStatuses.Disabled);
            if (change == "mfa")
                await services.GetRequiredService<IIdentityUserStore>().SetTwoFactorAsync(fixture.UserId, false);
            if (change == "record-mfa" || change.StartsWith("session-", StringComparison.Ordinal))
            {
                var store = services.GetRequiredService<IAuthSessionStore>();
                var record = (await store.GetByUserAsync(fixture.UserId)).Single();
                var changed = change switch
                {
                    "record-mfa" => record with { ClaimsJson = "[]" },
                    "session-expiry" => record with { RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
                    "session-inactivity" => record with { LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-31) },
                    "session-absolute" => record with { CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) },
                    "session-revoked" => record with { RevokedAt = DateTimeOffset.UtcNow },
                    _ => record
                };
                await store.SaveAsync(changed);
            }
            if (change == "role-deleted")
            {
                var store = services.GetRequiredService<ITenantRoleStore>();
                foreach (var assigned in await store.GetRolesForUserAsync(fixture.OrganizationId, fixture.UserId))
                    await store.DeleteRoleAsync(fixture.OrganizationId, assigned.RoleId);
            }
            if (change == "role")
            {
                var roles = services.GetRequiredService<ITenantRoleStore>();
                var assigned = await roles.GetRolesForUserAsync(fixture.OrganizationId, fixture.UserId);
                await roles.RevokeRolesAsync(fixture.OrganizationId, fixture.UserId, assigned.Select(r => r.RoleId).ToArray());
            }
        });
        using var staleAccess = await fixture.GetIdentityAsync(issued.AccessToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, staleAccess.StatusCode);
        using var staleRefresh = await fixture.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = issued.RefreshToken });
        Assert.AreEqual(HttpStatusCode.Unauthorized, staleRefresh.StatusCode);
    }

    [TestMethod]
    [DataRow("customer-audience")]
    [DataRow("expired")]
    [DataRow("no-mfa")]
    [DataRow("pre-tenant")]
    [DataRow("another-organization")]
    [DataRow("credential-subject")]
    public async Task SignedButInvalidSupportTokensCannotSubstituteForManagementSessions(string substitution)
    {
        await using var fixture = await ManagementIdentityHttpFixture.CreateAsync();
        var issued = await fixture.LoginAsync();
        var original = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);
        var claims = original.Claims.Where(c => c.Type is not ("aud" or "iss" or "exp" or "nbf" or "iat")).ToList();
        if (substitution == "no-mfa") claims.RemoveAll(c => c.Type == "amr");
        if (substitution == "pre-tenant") claims.Add(new Claim("pt", "1"));
        if (substitution == "another-organization")
        {
            claims.RemoveAll(c => c.Type == "tid" || c.Type == ManagementClaimTypes.OrganizationId);
            var another = Guid.NewGuid().ToString();
            claims.Add(new Claim("tid", another));
            claims.Add(new Claim(ManagementClaimTypes.OrganizationId, another));
        }
        if (substitution == "credential-subject")
        {
            claims.RemoveAll(c => c.Type == ManagementClaimTypes.SubjectType);
            claims.Add(new Claim(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Credential));
        }
        var forged = new JwtSecurityToken(fixture.Issuer, substitution == "customer-audience" ? "customer-api" : fixture.Audience,
            claims, DateTime.UtcNow.AddMinutes(-10), substitution == "expired" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(fixture.SigningKey)), SecurityAlgorithms.HmacSha256));
        using var response = await fixture.GetIdentityAsync(new JwtSecurityTokenHandler().WriteToken(forged));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task OtpReplayWrongCodeAndWrongPurposeCannotCompleteMfa()
    {
        await using var fixture = await ManagementIdentityHttpFixture.CreateAsync();
        using var challengeResponse = await fixture.Client.PostAsJsonAsync("/api/auth/password-login",
            new { email = ManagementIdentityHttpFixture.Email, password = ManagementIdentityHttpFixture.Password });
        var challenge = await ManagementIdentityHttpFixture.ReadDataAsync<AuthResultResponse>(challengeResponse);
        var code = fixture.Sender.Messages.Last().Code!;
        var wrongCode = code[0] == '0' ? "1" + code[1..] : "0" + code[1..];
        using var invalid = await fixture.Client.PostAsJsonAsync("/api/auth/2fa/complete-login",
            new { email = ManagementIdentityHttpFixture.Email, challengeId = challenge.TwoFactorChallengeId, code = wrongCode });
        Assert.AreEqual(HttpStatusCode.Unauthorized, invalid.StatusCode);
        using var complete = await fixture.Client.PostAsJsonAsync("/api/auth/2fa/complete-login",
            new { email = ManagementIdentityHttpFixture.Email, challengeId = challenge.TwoFactorChallengeId, code });
        Assert.AreEqual(HttpStatusCode.OK, complete.StatusCode);
        using var replay = await fixture.Client.PostAsJsonAsync("/api/auth/2fa/complete-login",
            new { email = ManagementIdentityHttpFixture.Email, challengeId = challenge.TwoFactorChallengeId, code });
        Assert.AreEqual(HttpStatusCode.Unauthorized, replay.StatusCode);
        await fixture.WithScopeAsync(async services =>
        {
            var other = await services.GetRequiredService<IOtpService>().CreateChallengeAsync(
                new OtpChallengeRequest(SenderChannel.Email, ManagementIdentityHttpFixture.Email, SenderPurpose.UserRegistration, null));
            using var purposeMismatch = await fixture.Client.PostAsJsonAsync("/api/auth/2fa/complete-login",
                new { email = ManagementIdentityHttpFixture.Email, challengeId = other.ChallengeId, code = fixture.Sender.Messages.Last().Code });
            Assert.AreEqual(HttpStatusCode.Unauthorized, purposeMismatch.StatusCode);
        });
    }

    [TestMethod]
    [DataRow("/api/auth/start-email-password-registration")]
    [DataRow("/api/auth/complete-email-password-registration")]
    [DataRow("/api/auth/startotp")]
    [DataRow("/api/auth/request-password-reset")]
    [DataRow("/api/management/identity/bootstrap")]
    [DataRow("/api/management/support-invitations/accept")]
    public async Task PublicEnrollmentRecoveryAndBootstrapAreNotInstalled(string path)
    {
        await using var fixture = await ManagementIdentityHttpFixture.CreateAsync();
        using var response = await fixture.Client.PostAsJsonAsync(path, new { email = "attacker@example.test", password = "ignored" });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        await fixture.WithScopeAsync(async services =>
        {
            Assert.IsNull(await services.GetRequiredService<IIdentityUserStore>().FindByEmailAsync("attacker@example.test"));
        });
    }

    [TestMethod]
    public async Task UnderlyingIdentityEnrollmentAlsoFailsClosed()
    {
        await using var fixture = await ManagementIdentityHttpFixture.CreateAsync();
        await fixture.WithScopeAsync(async services =>
        {
            await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => services.GetRequiredService<IIdentityAuthService>()
                .RegisterAsync(new RegisterUserRequest("attacker@example.test", null, "Fixture-Only!83")));
            Assert.IsNull(await services.GetRequiredService<IIdentityUserStore>().FindByEmailAsync("attacker@example.test"));
        });
    }

    [TestMethod]
    public async Task AdministratorDisablesAnotherSupportUserAndRevokesTheirSessions()
    {
        await using var fixture = await ManagementIdentityHttpFixture.CreateAsync();
        var administrator = await fixture.LoginAsync();
        Guid supportId = default;
        const string supportEmail = "supporter@example.test";
        await fixture.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<IIdentityUserStore>();
            var created = await users.CreateAsync(new RegisterUserRequest(supportEmail, null, ManagementIdentityHttpFixture.Password));
            Assert.IsTrue(created.Succeeded);
            supportId = created.User!.UserId;
            await users.SetTwoFactorAsync(supportId, true, "email");
            var roles = services.GetRequiredService<ITenantRoleStore>();
            var role = await roles.CreateRoleAsync(fixture.OrganizationId, ManagementIdentityRoles.Support);
            await roles.EnsureTenantMembershipAndGrantRolesAsync(new TenantMembershipRoleBootstrapRequest(
                fixture.OrganizationId, supportId, RoleIds: [role.RoleId], SetAsDefault: true, UserEmail: supportEmail));
        });
        var support = await fixture.LoginAsync(supportEmail);
        using var denied = new HttpRequestMessage(HttpMethod.Post, "/api/management/support-users/" + fixture.UserId + "/disable");
        denied.Headers.Authorization = new AuthenticationHeaderValue("Bearer", support.AccessToken);
        using var deniedResponse = await fixture.Client.SendAsync(denied);
        Assert.AreEqual(HttpStatusCode.Unauthorized, deniedResponse.StatusCode);
        using var self = new HttpRequestMessage(HttpMethod.Post, "/api/management/support-users/" + fixture.UserId + "/disable");
        self.Headers.Authorization = new AuthenticationHeaderValue("Bearer", administrator.AccessToken);
        using var selfResponse = await fixture.Client.SendAsync(self);
        Assert.AreEqual(HttpStatusCode.Unauthorized, selfResponse.StatusCode);
        using var disable = new HttpRequestMessage(HttpMethod.Post, "/api/management/support-users/" + supportId + "/disable");
        disable.Headers.Authorization = new AuthenticationHeaderValue("Bearer", administrator.AccessToken);
        using var disabled = await fixture.Client.SendAsync(disable);
        Assert.AreEqual(HttpStatusCode.NoContent, disabled.StatusCode);
        using var oldAccess = await fixture.GetIdentityAsync(support.AccessToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldAccess.StatusCode);
        using var oldRefresh = await fixture.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = support.RefreshToken });
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        using var adminStillActive = await fixture.GetIdentityAsync(administrator.AccessToken);
        Assert.AreEqual(HttpStatusCode.OK, adminStillActive.StatusCode);
        await fixture.WithScopeAsync(async services =>
        {
            Assert.IsFalse((await services.GetRequiredService<ITenantMembershipStore>()
                .GetTenantForUserAsync(supportId, fixture.OrganizationId))!.IsActive);
            Assert.IsTrue((await services.GetRequiredService<IAuthSessionStore>().GetByUserAsync(supportId))
                .All(x => x.RevokedAt.HasValue));
        });
    }

    [TestMethod]
    public async Task DirectTokenIssuanceWithoutVerifiedMfaCannotCreateASupportSession()
    {
        await using var fixture = await ManagementIdentityHttpFixture.CreateAsync();
        await fixture.WithScopeAsync(async services =>
        {
            await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => services.GetRequiredService<ITokenService>()
                .CreateAccessTokenAsync(fixture.UserId, fixture.OrganizationId, []));
            Assert.HasCount(0, await services.GetRequiredService<IAuthSessionStore>().GetByUserAsync(fixture.UserId));
            await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => services.GetRequiredService<IIdentityAuthService>()
                .StartEmailPasswordRegistrationAsync("attacker@example.test"));
        });
    }

}
