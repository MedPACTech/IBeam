using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Tenants;

public sealed class TenantSelectionService : ITenantSelectionService
{
    private readonly ITenantMembershipStore _tenants;
    private readonly ITokenService _tokenService;
    private readonly ITenantInfoResolver _tenantInfoResolver;
    private readonly ITenantExtensionCoordinator _tenantExtensions;

    public TenantSelectionService(
        ITenantMembershipStore tenants,
        ITokenService tokenService)
        : this(
            tenants,
            tokenService,
            new TenantInfoResolver(new NoOpTenantMetadataProvider()),
            new NoOpTenantExtensionCoordinator())
    {
    }

    public TenantSelectionService(
        ITenantMembershipStore tenants,
        ITokenService tokenService,
        ITenantInfoResolver tenantInfoResolver,
        ITenantExtensionCoordinator tenantExtensions)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _tenantInfoResolver = tenantInfoResolver ?? throw new ArgumentNullException(nameof(tenantInfoResolver));
        _tenantExtensions = tenantExtensions ?? throw new ArgumentNullException(nameof(tenantExtensions));
    }

    public async Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var identityTenants = await _tenants.GetTenantsForUserAsync(userId, ct).ConfigureAwait(false);
        await EnsureTenantExtensionsAsync(identityTenants, userId, TenantExtensionOperations.Listed, ct).ConfigureAwait(false);

        return await _tenantInfoResolver.EnrichAsync(identityTenants, ct).ConfigureAwait(false);
    }

    public async Task<TokenResult> SelectTenantAsync(TenantSelectionRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.UserId == Guid.Empty) throw new IdentityValidationException("userId is required.");
        if (request.TenantId == Guid.Empty) throw new IdentityValidationException("tenantId is required.");

        var identityTenant = await _tenants.GetTenantForUserAsync(request.UserId, request.TenantId, ct)
            .ConfigureAwait(false);
        var tenant = await _tenantInfoResolver
            .EnrichAsync(identityTenant, ct)
            .ConfigureAwait(false);
        if (tenant is null || !tenant.IsActive)
            throw new IdentityUnauthorizedException("No active tenant membership.");

        if (request.SetAsDefault)
            await _tenants.SetDefaultTenantAsync(request.UserId, request.TenantId, ct);

        await _tenantExtensions.EnsureExtensionAsync(
            IdentityTenant.FromTenantInfo(identityTenant!),
            TenantExtensionContext.Create(TenantExtensionOperations.Selected, authUserId: request.UserId),
            ct).ConfigureAwait(false);

        var claims = new List<ClaimItem>
        {
            new("tid", tenant.TenantId.ToString("D"))
        };

        // optional role claims
        if (tenant.Roles is not null)
        {
            foreach (var role in tenant.Roles.Where(r => !string.IsNullOrWhiteSpace(r)))
                claims.Add(new ClaimItem("role", role));
        }

        if (tenant.RoleIds is not null)
        {
            foreach (var roleId in tenant.RoleIds.Where(x => x != Guid.Empty).Distinct())
                claims.Add(new ClaimItem("rid", roleId.ToString("D")));
        }

        return await _tokenService.CreateAccessTokenAsync(
            userId: request.UserId,
            tenantId: tenant.TenantId,
            claims: claims,
            ct: ct);
    }

    public Task<TokenResult> SwitchTenantAsync(TenantSelectionRequest request, CancellationToken ct = default)
        => SelectTenantAsync(request, ct);

    private async Task EnsureTenantExtensionsAsync(
        IReadOnlyList<TenantInfo> tenants,
        Guid userId,
        string operation,
        CancellationToken ct)
    {
        foreach (var tenant in tenants)
        {
            await _tenantExtensions.EnsureExtensionAsync(
                IdentityTenant.FromTenantInfo(tenant),
                TenantExtensionContext.Create(operation, authUserId: userId),
                ct).ConfigureAwait(false);
        }
    }
}
