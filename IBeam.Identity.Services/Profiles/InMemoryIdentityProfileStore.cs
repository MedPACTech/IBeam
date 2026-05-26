using System.Collections.Concurrent;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Profiles;

public sealed class InMemoryIdentityProfileStore : IIdentityProfileStore
{
    private readonly ConcurrentDictionary<Guid, Dictionary<string, string>> _profiles = new();

    public Task<IdentityProfileExtensions> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var attrs = _profiles.TryGetValue(userId, out var existing)
            ? new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(new IdentityProfileExtensions(userId, attrs, null));
    }

    public Task<IdentityProfileExtensions> UpsertAsync(Guid userId, IReadOnlyDictionary<string, string> attributes, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var merged = _profiles.AddOrUpdate(
            userId,
            _ => CreateSanitized(attributes),
            (_, current) => Merge(current, attributes));

        return Task.FromResult(new IdentityProfileExtensions(userId, new Dictionary<string, string>(merged, StringComparer.OrdinalIgnoreCase), now));
    }

    public Task RemoveKeysAsync(Guid userId, IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        if (!_profiles.TryGetValue(userId, out var existing))
            return Task.CompletedTask;

        lock (existing)
        {
            foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
                existing.Remove(key.Trim());
        }

        return Task.CompletedTask;
    }

    private static Dictionary<string, string> Merge(Dictionary<string, string> current, IReadOnlyDictionary<string, string> incoming)
    {
        lock (current)
        {
            foreach (var pair in incoming)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                var key = pair.Key.Trim();
                var value = pair.Value?.Trim() ?? string.Empty;
                current[key] = value;
            }

            return current;
        }
    }

    private static Dictionary<string, string> CreateSanitized(IReadOnlyDictionary<string, string> source)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            map[pair.Key.Trim()] = pair.Value?.Trim() ?? string.Empty;
        }

        return map;
    }
}
