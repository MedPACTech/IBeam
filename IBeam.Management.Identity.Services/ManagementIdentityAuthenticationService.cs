using System.Security.Claims;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Services.Abstractions;

namespace IBeam.Management.Identity.Services;

public interface IManagementIdentityAuthenticationService
{
    Task<AuthResultResponse> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default);
    Task<AuthResultResponse> CompleteMfaAsync(string email, string challengeId, string code, CancellationToken ct = default);
    Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}

public sealed class ManagementIdentityAuthenticationService(
    IIdentityAuthService password,
    ITokenService tokens,
    ManagementIdentityMembershipService members,
    ManagementIdentitySessionService sessions,
    IServiceOperationExecutor operations) : IManagementIdentityAuthenticationService
{
    [IBeamOperation("management.identity.password-login", Permission = false)]
    public Task<AuthResultResponse> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            await members.RequireByEmailAsync(request.Email, token);
            // Remember-device bypass/extended lifetimes are not enabled for support.
            var result = await password.PasswordLoginAsync(request with { RememberDevice = false }, token);
            if (!result.RequiresTwoFactor || result.Token is not null || result.PreTenantToken is not null)
                throw new ManagementAccessDeniedException();
            return result;
        }, new ServiceOperationExecutionOptions { PermissionEnabled = false }, ct);

    [IBeamOperation("management.identity.mfa.complete", Permission = false)]
    public Task<AuthResultResponse> CompleteMfaAsync(string email, string challengeId, string code, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            await members.RequireByEmailAsync(email, token);
            var result = await password.CompleteTwoFactorLoginAsync(email, challengeId, code, token);
            if (result.Token is null || result.RequiresTenantSelection || result.PreTenantToken is not null)
                throw new ManagementAccessDeniedException();
            await sessions.ValidateRefreshAsync(result.Token.RefreshToken ?? "", token);
            return result;
        }, new ServiceOperationExecutionOptions { PermissionEnabled = false }, ct);

    [IBeamOperation("management.identity.refresh", Permission = false)]
    public Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            await sessions.ValidateRefreshAsync(refreshToken, token);
            return await tokens.RefreshAccessTokenAsync(refreshToken, token);
        }, new ServiceOperationExecutionOptions { PermissionEnabled = false }, ct);

    [IBeamOperation("management.identity.logout", Permission = false)]
    public Task LogoutAsync(ClaimsPrincipal principal, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            var member = await sessions.ValidateAccessAsync(principal, token);
            await tokens.RevokeSessionAsync(member.User.UserId, principal.FindFirst("sid")!.Value, token);
        }, new ServiceOperationExecutionOptions { PermissionEnabled = false }, ct);
}
