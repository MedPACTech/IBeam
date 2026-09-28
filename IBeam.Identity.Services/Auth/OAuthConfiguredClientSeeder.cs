using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Auth;

// A first-party client (a CLI, another internal tool) needs a single OAuth client_id shared
// across every tenant - IOAuthClientAdministrationService only creates tenant-scoped clients, so
// it doesn't fit. IBeam:Identity:OAuthServer:Clients (OAuthClientRegistrationOptions) already
// supports declaring a client with TenantId: null; this just takes that same declarative config
// and idempotently upserts it into whichever IOAuthClientStore is actually registered (Azure
// Table or EF), so the client survives restarts instead of only existing in-process the way
// InMemoryOAuthClientStore's own copy of this config does.
public static class OAuthConfiguredClientSeederExtensions
{
    public static async Task SeedConfiguredOAuthClientsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<OAuthAuthorizationServerOptions>>().Value;
        options.Validate();
        if (!options.Enabled || options.Clients.Count == 0)
            return;

        var store = scope.ServiceProvider.GetRequiredService<IOAuthClientStore>();
        if (store is InMemoryOAuthClientStore)
            return; // Already the single source of truth for this same config - nothing to sync.

        foreach (var configured in options.Clients)
        {
            var existing = await store.GetAsync(configured.ClientId, ct).ConfigureAwait(false);
            if (existing is null)
            {
                await store.CreateAsync(ToRecord(configured, DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
            }
            else if (!Matches(existing, configured))
            {
                await store.UpdateAsync(
                    ToRecord(configured, existing.CreatedUtc) with { UpdatedUtc = DateTimeOffset.UtcNow },
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static OAuthClientRecord ToRecord(OAuthClientRegistrationOptions options, DateTimeOffset createdUtc) =>
        new(
            options.ClientId,
            options.TenantId,
            options.DisplayName,
            options.ClientType,
            options.RedirectUris.ToArray(),
            options.AllowedGrantTypes.ToArray(),
            options.AllowedScopes.ToArray(),
            options.AllowedResources.ToArray(),
            options.RequirePkce,
            options.Status,
            options.ClientSecretHash,
            options.ClientSecretHashAlgorithm,
            createdUtc,
            ClientSecretExpiresUtc: options.ClientSecretExpiresUtc,
            DisabledUtc: options.Status == OAuthClientStatuses.Disabled ? DateTimeOffset.UtcNow : null,
            RevokedUtc: options.Status == OAuthClientStatuses.Revoked ? DateTimeOffset.UtcNow : null,
            DeviceVerificationUri: options.DeviceVerificationUri);

    private static bool Matches(OAuthClientRecord existing, OAuthClientRegistrationOptions configured) =>
        existing.TenantId == configured.TenantId &&
        existing.DisplayName == configured.DisplayName &&
        existing.ClientType == configured.ClientType &&
        existing.RequirePkce == configured.RequirePkce &&
        existing.Status == configured.Status &&
        existing.ClientSecretHash == configured.ClientSecretHash &&
        existing.DeviceVerificationUri == configured.DeviceVerificationUri &&
        existing.RedirectUris.SequenceEqual(configured.RedirectUris, StringComparer.Ordinal) &&
        existing.AllowedGrantTypes.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(configured.AllowedGrantTypes.OrderBy(x => x, StringComparer.Ordinal)) &&
        existing.AllowedScopes.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(configured.AllowedScopes.OrderBy(x => x, StringComparer.Ordinal)) &&
        existing.AllowedResources.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(configured.AllowedResources.OrderBy(x => x, StringComparer.Ordinal));
}
