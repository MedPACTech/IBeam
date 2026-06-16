using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Tenants;

public sealed class TenantInfoResolver : ITenantInfoResolver
{
    private readonly ITenantMetadataProvider _metadataProvider;

    public TenantInfoResolver(ITenantMetadataProvider metadataProvider)
    {
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
    }

    public async Task<TenantInfo?> EnrichAsync(TenantInfo? tenant, CancellationToken ct = default)
    {
        if (tenant is null)
            return null;

        var metadata = await _metadataProvider.GetTenantMetadataAsync(tenant.TenantId, ct).ConfigureAwait(false);
        return ApplyMetadata(tenant, metadata);
    }

    public async Task<IReadOnlyList<TenantInfo>> EnrichAsync(IReadOnlyList<TenantInfo> tenants, CancellationToken ct = default)
    {
        if (tenants is null || tenants.Count == 0)
            return Array.Empty<TenantInfo>();

        var results = new List<TenantInfo>(tenants.Count);
        foreach (var tenant in tenants)
        {
            var enriched = await EnrichAsync(tenant, ct).ConfigureAwait(false);
            if (enriched is not null)
                results.Add(enriched);
        }

        return results;
    }

    private static TenantInfo ApplyMetadata(TenantInfo tenant, TenantMetadata? metadata)
    {
        if (metadata is null)
            return tenant;

        var name =
            FirstNonEmpty(metadata.DisplayName, metadata.Name) ??
            tenant.Name;

        var isActive = tenant.IsActive && (metadata.IsActive ?? true);

        return tenant with
        {
            Name = name,
            IsActive = isActive
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
