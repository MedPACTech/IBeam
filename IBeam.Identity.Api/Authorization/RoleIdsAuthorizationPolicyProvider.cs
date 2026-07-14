using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Api.Authorization;

public sealed class RoleIdsAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "ibeam:roleids:";
    public const string PermissionPolicyPrefix = "RequirePermission:";
    public const string ModulePolicyPrefix = "RequireModule:";
    public const string ResourcePolicyPrefix = "RequireResource:";

    private readonly DefaultAuthorizationPolicyProvider _fallbackProvider;

    public RoleIdsAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        => _fallbackProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        => _fallbackProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (string.IsNullOrWhiteSpace(policyName))
            return _fallbackProvider.GetPolicyAsync(policyName);

        if (policyName.StartsWith(PermissionPolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var permissionName = policyName[PermissionPolicyPrefix.Length..].Trim();
            if (permissionName.Length == 0)
                return Task.FromResult<AuthorizationPolicy?>(null);

            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new RequirePermissionRequirement(permissionName))
                .Build());
        }

        if (policyName.StartsWith(ModulePolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = SplitPolicyParts(policyName[ModulePolicyPrefix.Length..]);
            if (parts.Count == 0)
                return Task.FromResult<AuthorizationPolicy?>(null);

            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new RequireModuleRequirement(parts[0], parts.ElementAtOrDefault(1) ?? "view"))
                .Build());
        }

        if (policyName.StartsWith(ResourcePolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = SplitPolicyParts(policyName[ResourcePolicyPrefix.Length..]);
            if (parts.Count < 2)
                return Task.FromResult<AuthorizationPolicy?>(null);

            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new RequireResourceRequirement(parts[0], parts[1], parts.ElementAtOrDefault(2) ?? "view"))
                .Build());
        }

        if (!policyName.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
            return _fallbackProvider.GetPolicyAsync(policyName);

        var csv = policyName[PolicyPrefix.Length..];
        var parsed = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();

        if (parsed.Count == 0)
            return Task.FromResult<AuthorizationPolicy?>(null);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new RequireRoleIdsRequirement(parsed))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    private static IReadOnlyList<string> SplitPolicyParts(string value)
        => value
            .Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
}
