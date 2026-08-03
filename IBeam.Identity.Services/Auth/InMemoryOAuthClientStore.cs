using System.Collections.Concurrent;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Auth;

public sealed class InMemoryOAuthClientStore : IOAuthClientStore
{
    private readonly ConcurrentDictionary<string, OAuthClientRecord> _clients = new(StringComparer.Ordinal);

    public InMemoryOAuthClientStore(IOptions<OAuthAuthorizationServerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();

        foreach (var configuredClient in options.Value.Clients)
        {
            var client = ToRecord(configuredClient);
            if (!_clients.TryAdd(client.ClientId, client))
                throw new InvalidOperationException($"OAuth client id '{client.ClientId}' is configured more than once.");
        }
    }

    public Task<OAuthClientRecord?> GetAsync(string clientId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(clientId))
            return Task.FromResult<OAuthClientRecord?>(null);

        return Task.FromResult(_clients.TryGetValue(clientId.Trim(), out var client) ? client : null);
    }

    public Task<IReadOnlyList<OAuthClientRecord>> ListByTenantAsync(
        Guid? tenantId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<OAuthClientRecord> clients = _clients.Values
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ClientId, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(clients);
    }

    public Task<OAuthClientRecord> CreateAsync(OAuthClientRecord client, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(client);
        var normalized = Normalize(client);
        if (!_clients.TryAdd(normalized.ClientId, normalized))
            throw new IdentityValidationException($"OAuth client id '{normalized.ClientId}' already exists.");

        return Task.FromResult(normalized);
    }

    public Task<OAuthClientRecord> UpdateAsync(OAuthClientRecord client, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(client);
        var normalized = Normalize(client);
        if (!_clients.ContainsKey(normalized.ClientId))
            throw new IdentityNotFoundException($"OAuth client '{normalized.ClientId}' was not found.");

        _clients[normalized.ClientId] = normalized;
        return Task.FromResult(normalized);
    }

    private static OAuthClientRecord ToRecord(OAuthClientRegistrationOptions options) =>
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
            DateTimeOffset.UtcNow,
            ClientSecretExpiresUtc: options.ClientSecretExpiresUtc,
            DisabledUtc: options.Status == OAuthClientStatuses.Disabled ? DateTimeOffset.UtcNow : null,
            RevokedUtc: options.Status == OAuthClientStatuses.Revoked ? DateTimeOffset.UtcNow : null);

    private static OAuthClientRecord Normalize(OAuthClientRecord client)
    {
        var registration = new OAuthClientRegistrationOptions
        {
            ClientId = client.ClientId,
            TenantId = client.TenantId,
            DisplayName = client.DisplayName,
            ClientType = client.ClientType,
            RedirectUris = client.RedirectUris.ToList(),
            AllowedGrantTypes = client.AllowedGrantTypes.ToList(),
            AllowedScopes = client.AllowedScopes.ToList(),
            AllowedResources = client.AllowedResources.ToList(),
            RequirePkce = client.RequirePkce,
            Status = client.Status,
            ClientSecretHash = client.ClientSecretHash,
            ClientSecretHashAlgorithm = client.ClientSecretHashAlgorithm,
            ClientSecretExpiresUtc = client.ClientSecretExpiresUtc
        };
        registration.NormalizeAndValidate();

        return client with
        {
            ClientId = registration.ClientId,
            DisplayName = registration.DisplayName,
            ClientType = registration.ClientType,
            RedirectUris = registration.RedirectUris.ToArray(),
            AllowedGrantTypes = registration.AllowedGrantTypes.ToArray(),
            AllowedScopes = registration.AllowedScopes.ToArray(),
            AllowedResources = registration.AllowedResources.ToArray(),
            Status = registration.Status,
            ClientSecretHash = registration.ClientSecretHash,
            ClientSecretHashAlgorithm = registration.ClientSecretHashAlgorithm,
            ClientSecretExpiresUtc = registration.ClientSecretExpiresUtc
        };
    }
}
