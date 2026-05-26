using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Authorization;

public interface IPermissionGrantResolver
{
    Task<PermissionGrantSet> ResolveAsync(
        Guid tenantId,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default);
}

public sealed class PermissionGrantResolver : IPermissionGrantResolver
{
    private readonly IOptionsMonitor<PermissionAccessOptions> _options;
    private readonly IPermissionAccessStore _store;

    public PermissionGrantResolver(
        IOptionsMonitor<PermissionAccessOptions> options,
        IPermissionAccessStore store)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<PermissionGrantSet> ResolveAsync(
        Guid tenantId,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return PermissionGrantSet.Empty;

        var names = permissionNames?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        var ids = permissionIds?
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList() ?? [];

        if (names.Count == 0 && ids.Count == 0)
            return PermissionGrantSet.Empty;

        var mergedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mergedIds = new HashSet<Guid>();

        foreach (var entry in _options.CurrentValue.Mappings)
        {
            if (entry is null)
                continue;

            if (entry.TenantId.HasValue && entry.TenantId.Value != tenantId)
                continue;

            var nameMatch = !string.IsNullOrWhiteSpace(entry.PermissionName) &&
                names.Contains(entry.PermissionName.Trim(), StringComparer.OrdinalIgnoreCase);

            var idMatch = entry.PermissionId.HasValue &&
                ids.Contains(entry.PermissionId.Value);

            if (!nameMatch && !idMatch)
                continue;

            foreach (var rn in entry.RoleNames.Where(x => !string.IsNullOrWhiteSpace(x)))
                mergedNames.Add(rn.Trim());

            foreach (var rid in entry.RoleIds.Where(x => x != Guid.Empty))
                mergedIds.Add(rid);
        }

        var storeGrant = await _store.ResolveGrantsAsync(tenantId, names, ids, ct);
        foreach (var roleName in storeGrant.RoleNames.Where(x => !string.IsNullOrWhiteSpace(x)))
            mergedNames.Add(roleName.Trim());
        foreach (var roleId in storeGrant.RoleIds.Where(x => x != Guid.Empty))
            mergedIds.Add(roleId);

        return new PermissionGrantSet(mergedNames.ToList(), mergedIds.ToList());
    }
}
