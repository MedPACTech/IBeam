using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.AspNetCore.Identity;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using ElCamino.AspNetCore.Identity.AzureTable;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableIdentityProfileStore : IIdentityProfileStore
{
    private readonly UserStore<ApplicationUser, ApplicationRole, IdentityCloudContext> _store;

    public AzureTableIdentityProfileStore(UserStore<ApplicationUser, ApplicationRole, IdentityCloudContext> store)
    {
        _store = store;
    }

    public async Task<IdentityProfileExtensions> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _store.FindByIdAsync(userId.ToString("D"));
        if (user is null)
            throw new IdentityValidationException("User not found.");

        var map = Deserialize(user.ProfileExtensionsJson);
        return new IdentityProfileExtensions(userId, map, user.ProfileExtensionsUpdatedAtUtc);
    }

    public async Task<IdentityProfileExtensions> UpsertAsync(Guid userId, IReadOnlyDictionary<string, string> attributes, CancellationToken ct = default)
    {
        var user = await _store.FindByIdAsync(userId.ToString("D"));
        if (user is null)
            throw new IdentityValidationException("User not found.");

        var current = Deserialize(user.ProfileExtensionsJson);
        foreach (var pair in attributes)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            current[pair.Key.Trim()] = pair.Value?.Trim() ?? string.Empty;
        }

        user.ProfileExtensionsJson = JsonSerializer.Serialize(current);
        user.ProfileExtensionsUpdatedAtUtc = DateTimeOffset.UtcNow;

        var result = await _store.UpdateAsync(user);
        if (!result.Succeeded)
            throw new IdentityProviderException("AzureTable", "Failed to update profile extensions.");

        return new IdentityProfileExtensions(userId, current, user.ProfileExtensionsUpdatedAtUtc);
    }

    public async Task RemoveKeysAsync(Guid userId, IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        var user = await _store.FindByIdAsync(userId.ToString("D"));
        if (user is null)
            throw new IdentityValidationException("User not found.");

        var current = Deserialize(user.ProfileExtensionsJson);
        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
            current.Remove(key.Trim());

        user.ProfileExtensionsJson = JsonSerializer.Serialize(current);
        user.ProfileExtensionsUpdatedAtUtc = DateTimeOffset.UtcNow;

        var result = await _store.UpdateAsync(user);
        if (!result.Succeeded)
            throw new IdentityProviderException("AzureTable", "Failed to remove profile extension keys.");
    }

    private static Dictionary<string, string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
