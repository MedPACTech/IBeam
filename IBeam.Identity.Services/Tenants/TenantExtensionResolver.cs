using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Tenants;

public sealed class TenantExtensionResolver<TTenant> : ITenantExtensionResolver<TTenant>
    where TTenant : class, IIdentityTenantExtension
{
    private readonly ITenantExtensionStore<TTenant> _store;

    public TenantExtensionResolver(ITenantExtensionStore<TTenant> store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<TTenant?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");

        return _store.FindByTenantIdAsync(tenantId, ct);
    }

    public async Task<TTenant> EnsureAsync(IdentityTenant identityTenant, TenantExtensionContext context, CancellationToken ct = default)
    {
        ValidateIdentityTenant(identityTenant);
        context ??= TenantExtensionContext.Create(TenantExtensionOperations.Ensure);

        var existing = await _store.FindByTenantIdAsync(identityTenant.TenantId, ct).ConfigureAwait(false);
        if (existing is null)
            return await _store.CreateAsync(identityTenant, context, ct).ConfigureAwait(false);

        return await _store.UpdateFromIdentityTenantAsync(existing, identityTenant, context, ct).ConfigureAwait(false);
    }

    private static void ValidateIdentityTenant(IdentityTenant identityTenant)
    {
        ArgumentNullException.ThrowIfNull(identityTenant);

        if (identityTenant.TenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
        if (string.IsNullOrWhiteSpace(identityTenant.Name))
            throw new IdentityValidationException("Tenant name is required.");
    }
}
