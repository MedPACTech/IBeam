using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.ApiCredentials;

public sealed class ApiCredentialRoleAssignmentValidator : IApiCredentialRoleAssignmentValidator
{
    private readonly ITenantRoleStore _tenantRoles;
    private readonly ApiCredentialOptions _options;

    public ApiCredentialRoleAssignmentValidator(
        ITenantRoleStore tenantRoles,
        IOptions<ApiCredentialOptions> options)
    {
        _tenantRoles = tenantRoles;
        _options = options.Value;
        _options.Validate();
    }

    public async Task ValidateAsync(
        Guid tenantId,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<string> roleNames,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");

        var denied = _options.DeniedCredentialRoleNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedRoleNames = roleNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();

        if (normalizedRoleNames.Any(x => x.Length > 128))
            throw new IdentityValidationException("API credential role names/scopes must be 128 characters or fewer.");

        var unsafeNames = normalizedRoleNames.Where(denied.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unsafeNames.Count > 0)
            throw new IdentityValidationException($"API credentials cannot be assigned unsafe human-management roles: {string.Join(", ", unsafeNames)}.");

        var ids = roleIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var activeRoles = await _tenantRoles.GetRolesAsync(tenantId, ct).ConfigureAwait(false);
        var roleMap = activeRoles.ToDictionary(x => x.RoleId, x => x);
        var missing = ids.Where(x => !roleMap.ContainsKey(x)).ToList();
        if (missing.Count > 0)
            throw new IdentityValidationException($"One or more roleIds do not exist in tenant '{tenantId}'.");

        var unsafeIdRoles = ids
            .Select(x => roleMap[x].Name)
            .Where(denied.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unsafeIdRoles.Count > 0)
            throw new IdentityValidationException($"API credentials cannot be assigned unsafe human-management roles: {string.Join(", ", unsafeIdRoles)}.");
    }
}
