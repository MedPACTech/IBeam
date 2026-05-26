using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Api.Authorization;

public sealed class RoleIdsAuthorizationPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "ibeam:roleids:";

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
        if (string.IsNullOrWhiteSpace(policyName) ||
            !policyName.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase))
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
}
