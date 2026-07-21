using System.Collections.Concurrent;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Invites;

public sealed class InMemoryTenantInviteStore : ITenantInviteStore
{
    private readonly ConcurrentDictionary<Guid, TenantInviteRecord> _byId = new();
    private readonly ConcurrentDictionary<string, Guid> _byTokenHash = new(StringComparer.Ordinal);

    public Task<TenantInviteRecord> CreateAsync(TenantInviteRecord invite, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _byId[invite.InviteId] = invite;
        _byTokenHash[invite.TokenHash] = invite.InviteId;
        return Task.FromResult(invite);
    }

    public Task<IReadOnlyList<TenantInviteRecord>> ListForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<TenantInviteRecord> list = _byId.Values
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedUtc)
            .ToList();
        return Task.FromResult(list);
    }

    public Task<TenantInviteRecord?> GetAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_byId.TryGetValue(inviteId, out var invite) && invite.TenantId == tenantId ? invite : null);
    }

    public Task<TenantInviteRecord?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_byTokenHash.TryGetValue(tokenHash, out var inviteId) && _byId.TryGetValue(inviteId, out var invite)
            ? invite
            : null);
    }

    public Task<TenantInviteRecord> UpdateAsync(TenantInviteRecord invite, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_byId.TryGetValue(invite.InviteId, out var existing) &&
            !string.Equals(existing.TokenHash, invite.TokenHash, StringComparison.Ordinal))
        {
            _byTokenHash.TryRemove(existing.TokenHash, out _);
        }

        _byId[invite.InviteId] = invite;
        _byTokenHash[invite.TokenHash] = invite.InviteId;
        return Task.FromResult(invite);
    }
}
