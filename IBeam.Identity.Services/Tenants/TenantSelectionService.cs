using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Tenants;

public sealed class TenantSelectionService : ITenantSelectionService
{
    private readonly ITenantMembershipStore _tenants;
    private readonly ITokenService _tokenService;

    public TenantSelectionService(
        ITenantMembershipStore tenants,
        ITokenService tokenService)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
    }

    public Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default)
        => _tenants.GetTenantsForUserAsync(userId, ct);

    public async Task<TokenResult> SelectTenantAsync(TenantSelectionRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.UserId == Guid.Empty) throw new IdentityValidationException("userId is required.");
        if (request.TenantId == Guid.Empty) throw new IdentityValidationException("tenantId is required.");

        var tenant = await _tenants.GetTenantForUserAsync(request.UserId, request.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            throw new IdentityUnauthorizedException("No active tenant membership.");

        if (request.SetAsDefault)
            await _tenants.SetDefaultTenantAsync(request.UserId, request.TenantId, ct);

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

        return await _tokenService.CreateAccessTokenAsync(
            userId: request.UserId,
            tenantId: tenant.TenantId,
            claims: claims,
            ct: ct);
    }

    public Task<TokenResult> SwitchTenantAsync(TenantSelectionRequest request, CancellationToken ct = default)
        => SelectTenantAsync(request, ct);
}
