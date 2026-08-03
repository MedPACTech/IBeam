using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.EntityFramework.Data;
using IBeam.Identity.Repositories.EntityFramework.OAuth.Entities;
using Microsoft.EntityFrameworkCore;

namespace IBeam.Identity.Repositories.EntityFramework.OAuth;

public sealed class EntityFrameworkOAuthConsentStore : IOAuthConsentStore
{
    private readonly IBeamIdentityDbContext _db;

    public EntityFrameworkOAuthConsentStore(IBeamIdentityDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<OAuthConsentRecord?> GetAsync(
        Guid userId,
        Guid tenantId,
        string clientId,
        string resource,
        CancellationToken ct = default)
    {
        var lookupKey = OAuthEntitySerialization.ConsentLookupKey(userId, tenantId, clientId, resource);
        var entity = await _db.OAuthConsents.AsNoTracking()
            .SingleOrDefaultAsync(x => x.LookupKey == lookupKey, ct)
            .ConfigureAwait(false);
        return entity is null ? null : Map(entity);
    }

    public async Task<OAuthConsentRecord> UpsertAsync(
        OAuthConsentRecord consent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consent);
        var lookupKey = OAuthEntitySerialization.ConsentLookupKey(
            consent.UserId,
            consent.TenantId,
            consent.ClientId,
            consent.Resource);
        var entity = await _db.OAuthConsents
            .SingleOrDefaultAsync(x => x.LookupKey == lookupKey, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = Map(consent, lookupKey);
            _db.OAuthConsents.Add(entity);
        }
        else
        {
            entity.ScopesJson = OAuthEntitySerialization.Write(consent.Scopes);
            entity.UpdatedUtc = consent.UpdatedUtc;
            entity.RevokedUtc = consent.RevokedUtc;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<bool> RevokeAsync(
        Guid userId,
        Guid tenantId,
        string clientId,
        string resource,
        DateTimeOffset revokedUtc,
        CancellationToken ct = default)
    {
        var lookupKey = OAuthEntitySerialization.ConsentLookupKey(userId, tenantId, clientId, resource);
        var updated = await _db.OAuthConsents
            .Where(x => x.LookupKey == lookupKey && x.RevokedUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.RevokedUtc, revokedUtc)
                    .SetProperty(x => x.UpdatedUtc, revokedUtc),
                ct)
            .ConfigureAwait(false);
        if (updated == 1)
            return true;

        return await _db.OAuthConsents.AsNoTracking()
            .AnyAsync(x => x.LookupKey == lookupKey, ct)
            .ConfigureAwait(false);
    }

    private static OAuthConsentEntity Map(OAuthConsentRecord consent, string lookupKey) =>
        new()
        {
            ConsentId = consent.ConsentId,
            LookupKey = lookupKey,
            UserId = consent.UserId,
            TenantId = consent.TenantId,
            ClientId = consent.ClientId,
            Resource = consent.Resource,
            ScopesJson = OAuthEntitySerialization.Write(consent.Scopes),
            CreatedUtc = consent.CreatedUtc,
            UpdatedUtc = consent.UpdatedUtc,
            RevokedUtc = consent.RevokedUtc
        };

    private static OAuthConsentRecord Map(OAuthConsentEntity entity) =>
        new(
            entity.ConsentId,
            entity.UserId,
            entity.TenantId,
            entity.ClientId,
            entity.Resource,
            OAuthEntitySerialization.Read(entity.ScopesJson),
            entity.CreatedUtc,
            entity.UpdatedUtc,
            entity.RevokedUtc);
}
