using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IOAuthEffectivePermissionResolver
{
    Task<OAuthEffectivePermissionResult> ResolveAsync(
        OAuthPermissionResolutionRequest request,
        CancellationToken ct = default);
}
