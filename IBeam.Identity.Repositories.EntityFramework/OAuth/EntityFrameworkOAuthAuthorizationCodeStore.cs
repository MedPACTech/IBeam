using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.EntityFramework.Data;
using IBeam.Identity.Repositories.EntityFramework.OAuth.Entities;
using Microsoft.EntityFrameworkCore;

namespace IBeam.Identity.Repositories.EntityFramework.OAuth;

public sealed class EntityFrameworkOAuthAuthorizationCodeStore : IOAuthAuthorizationCodeStore
{
    private readonly IBeamIdentityDbContext _db;

    public EntityFrameworkOAuthAuthorizationCodeStore(IBeamIdentityDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<OAuthAuthorizationCodeRecord> CreateAsync(
        OAuthAuthorizationCodeRecord authorizationCode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authorizationCode);
        var entity = Map(authorizationCode);
        _db.OAuthAuthorizationCodes.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<OAuthAuthorizationCodeRecord?> GetByHashAsync(
        string codeHash,
        CancellationToken ct = default)
    {
        var entity = await _db.OAuthAuthorizationCodes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CodeHash == codeHash, ct)
            .ConfigureAwait(false);
        return entity is null ? null : Map(entity);
    }

    public async Task<OAuthAuthorizationCodeRecord?> TryConsumeAsync(
        string codeHash,
        DateTimeOffset consumedUtc,
        CancellationToken ct = default)
    {
        var updated = await _db.OAuthAuthorizationCodes
            .Where(x =>
                x.CodeHash == codeHash &&
                x.ConsumedUtcTicks == null &&
                x.ExpiresUtcTicks > consumedUtc.UtcDateTime.Ticks)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ConsumedUtcTicks, consumedUtc.UtcDateTime.Ticks),
                ct)
            .ConfigureAwait(false);
        if (updated != 1)
            return null;

        var entity = await _db.OAuthAuthorizationCodes.AsNoTracking()
            .SingleAsync(x => x.CodeHash == codeHash, ct)
            .ConfigureAwait(false);
        return Map(entity);
    }

    private static OAuthAuthorizationCodeEntity Map(OAuthAuthorizationCodeRecord code) =>
        new()
        {
            CodeHash = code.CodeHash,
            ClientId = code.ClientId,
            RedirectUri = code.RedirectUri,
            UserId = code.UserId,
            TenantId = code.TenantId,
            ScopesJson = OAuthEntitySerialization.Write(code.Scopes),
            Resource = code.Resource,
            CodeChallenge = code.CodeChallenge,
            CodeChallengeMethod = code.CodeChallengeMethod,
            CreatedUtcTicks = code.CreatedUtc.UtcDateTime.Ticks,
            ExpiresUtcTicks = code.ExpiresUtc.UtcDateTime.Ticks,
            ConsumedUtcTicks = code.ConsumedUtc?.UtcDateTime.Ticks
        };

    private static OAuthAuthorizationCodeRecord Map(OAuthAuthorizationCodeEntity entity) =>
        new(
            entity.CodeHash,
            entity.ClientId,
            entity.RedirectUri,
            entity.UserId,
            entity.TenantId,
            OAuthEntitySerialization.Read(entity.ScopesJson),
            entity.Resource,
            entity.CodeChallenge,
            entity.CodeChallengeMethod,
            FromUtcTicks(entity.CreatedUtcTicks),
            FromUtcTicks(entity.ExpiresUtcTicks),
            entity.ConsumedUtcTicks is null ? null : FromUtcTicks(entity.ConsumedUtcTicks.Value));

    private static DateTimeOffset FromUtcTicks(long ticks) =>
        new(new DateTime(ticks, DateTimeKind.Utc));
}
