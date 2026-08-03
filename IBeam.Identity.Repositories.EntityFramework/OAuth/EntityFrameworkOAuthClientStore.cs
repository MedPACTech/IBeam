using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.EntityFramework.Data;
using IBeam.Identity.Repositories.EntityFramework.OAuth.Entities;
using Microsoft.EntityFrameworkCore;

namespace IBeam.Identity.Repositories.EntityFramework.OAuth;

public sealed class EntityFrameworkOAuthClientStore : IOAuthClientStore
{
    private readonly IBeamIdentityDbContext _db;

    public EntityFrameworkOAuthClientStore(IBeamIdentityDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<OAuthClientRecord?> GetAsync(string clientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var entity = await _db.OAuthClients.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ClientId == clientId.Trim(), ct)
            .ConfigureAwait(false);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<OAuthClientRecord>> ListByTenantAsync(
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var entities = await _db.OAuthClients.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.ClientId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return entities.Select(Map).ToList();
    }

    public async Task<OAuthClientRecord> CreateAsync(OAuthClientRecord client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var entity = Map(client);
        _db.OAuthClients.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<OAuthClientRecord> UpdateAsync(OAuthClientRecord client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var entity = await _db.OAuthClients
            .SingleOrDefaultAsync(x => x.ClientId == client.ClientId, ct)
            .ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"OAuth client '{client.ClientId}' was not found.");

        Apply(client, entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(entity);
    }

    private static OAuthClientEntity Map(OAuthClientRecord client)
    {
        var entity = new OAuthClientEntity { ClientId = client.ClientId };
        Apply(client, entity);
        return entity;
    }

    private static void Apply(OAuthClientRecord client, OAuthClientEntity entity)
    {
        entity.TenantId = client.TenantId;
        entity.DisplayName = client.DisplayName;
        entity.ClientType = client.ClientType;
        entity.RedirectUrisJson = OAuthEntitySerialization.Write(client.RedirectUris);
        entity.AllowedGrantTypesJson = OAuthEntitySerialization.Write(client.AllowedGrantTypes);
        entity.AllowedScopesJson = OAuthEntitySerialization.Write(client.AllowedScopes);
        entity.AllowedResourcesJson = OAuthEntitySerialization.Write(client.AllowedResources);
        entity.RequirePkce = client.RequirePkce;
        entity.Status = client.Status;
        entity.ClientSecretHash = client.ClientSecretHash;
        entity.ClientSecretHashAlgorithm = client.ClientSecretHashAlgorithm;
        entity.CreatedUtc = client.CreatedUtc;
        entity.UpdatedUtc = client.UpdatedUtc;
        entity.SecretRotatedUtc = client.SecretRotatedUtc;
        entity.ClientSecretExpiresUtc = client.ClientSecretExpiresUtc;
        entity.DisabledUtc = client.DisabledUtc;
        entity.RevokedUtc = client.RevokedUtc;
    }

    private static OAuthClientRecord Map(OAuthClientEntity entity) =>
        new(
            entity.ClientId,
            entity.TenantId,
            entity.DisplayName,
            entity.ClientType,
            OAuthEntitySerialization.Read(entity.RedirectUrisJson),
            OAuthEntitySerialization.Read(entity.AllowedGrantTypesJson),
            OAuthEntitySerialization.Read(entity.AllowedScopesJson),
            OAuthEntitySerialization.Read(entity.AllowedResourcesJson),
            entity.RequirePkce,
            entity.Status,
            entity.ClientSecretHash,
            entity.ClientSecretHashAlgorithm,
            entity.CreatedUtc,
            entity.UpdatedUtc,
            entity.SecretRotatedUtc,
            entity.ClientSecretExpiresUtc,
            entity.DisabledUtc,
            entity.RevokedUtc);
}
