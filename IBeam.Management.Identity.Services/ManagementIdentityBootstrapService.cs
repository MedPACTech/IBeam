using System.Net.Mail;
using IBeam.AccessControl;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Identity.Services;

public sealed class ManagementBootstrapRequest
{
    public string OrganizationName { get; init; } = string.Empty;
    public string AdministratorEmail { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public string InitialPassword { get; init; } = string.Empty;
    public override string ToString() => "Management bootstrap request (redacted)";
}

public sealed record ManagementBootstrapResult(Guid OrganizationId, Guid AdministratorUserId, bool CreatedUser);

public interface IManagementIdentityBootstrapService
{
    Task<ManagementBootstrapResult> BootstrapAsync(ManagementBootstrapRequest request, CancellationToken ct = default);
}

/// <summary>Trusted one-off setup only. Not registered as an HTTP operation or startup action.</summary>
public sealed class ManagementIdentityBootstrapService(
    IOptions<ManagementIdentityOptions> options,
    IIdentityTenantService tenants,
    IIdentityUserStore users,
    ITenantRoleService roles,
    ITenantMembershipStore memberships,
    IServiceOperationSystemContext system,
    IServiceOperationExecutor operations) : IManagementIdentityBootstrapService
{
    [IBeamOperation("management.identity.bootstrap", Permission = false)]
    public Task<ManagementBootstrapResult> BootstrapAsync(ManagementBootstrapRequest request, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            var configured = options.Value;
            if (!configured.BootstrapEnabled || configured.OrganizationId == Guid.Empty)
                throw new ManagementAccessDeniedException();
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.OrganizationName) ||
                !MailAddress.TryCreate(request.AdministratorEmail, out var address) ||
                !string.Equals(address.Address, request.AdministratorEmail, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("An organization name and a valid administrator email are required.");
            token.ThrowIfCancellationRequested();
            using var trusted = system.Enter("management.identity.bootstrap");
            var organization = await tenants.FindByIdAsync(configured.OrganizationId, token);
            if (organization is not null && !organization.IsActive)
                throw new ManagementAccessDeniedException();
            organization ??= await tenants.CreateAsync(request.OrganizationName.Trim(), configured.OrganizationId, ct: token);
            var user = await users.FindByEmailAsync(address.Address, token);
            var created = user is null;
            if (user is null)
            {
                if (string.IsNullOrWhiteSpace(request.InitialPassword))
                    throw new ArgumentException("An initial password from trusted secret input is required for a new administrator.");
                var result = await users.CreateAsync(new RegisterUserRequest(address.Address, null, request.InitialPassword), token);
                if (!result.Succeeded || result.User is null)
                    throw new InvalidOperationException("Management administrator creation failed; verify the configured password policy.");
                user = result.User;
            }
            var membership = await memberships.GetTenantForUserAsync(user.UserId, configured.OrganizationId, token);
            if (membership is { IsActive: false }) throw new ManagementAccessDeniedException();
            var catalog = await roles.GetRolesAsync(configured.OrganizationId, token);
            var adminRole = catalog.SingleOrDefault(x => x.Name == ManagementIdentityRoles.Administrator)
                ?? await roles.CreateRoleAsync(configured.OrganizationId, ManagementIdentityRoles.Administrator, token);
            if (!adminRole.IsActive) throw new ManagementAccessDeniedException();
            if (membership is null || !membership.Roles.Contains(ManagementIdentityRoles.Administrator, StringComparer.Ordinal))
                await roles.EnsureTenantMembershipAndGrantRolesAsync(new TenantMembershipRoleBootstrapRequest(
                    configured.OrganizationId, user.UserId, organization.Name, RoleIds: [adminRole.RoleId],
                    SetAsDefault: true, UserEmail: user.Email), token);
            // A new administrator must complete email OTP at sign-in. Reruns never reset passwords or re-enable MFA.
            if (created) await users.SetTwoFactorAsync(user.UserId, true, "email", token);
            return new ManagementBootstrapResult(configured.OrganizationId, user.UserId, created);
        }, new ServiceOperationExecutionOptions
        {
            OperationName = "management.identity.bootstrap",
            AuditAction = "management.identity.bootstrap",
            PermissionEnabled = false,
            AuditEnabled = true,
            TenantId = options.Value.OrganizationId
            // Never attach the request or its password to audit snapshots.
        }, ct);
}
