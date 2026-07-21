using IBeam.Identity.Interfaces;

namespace IBeam.Identity.Models;

public static class IdentityUserDefaults
{
    public static string? ResolveDisplayName(string? displayName, string? email, string? phoneNumber)
        => FirstNonEmpty(displayName, email, phoneNumber);

    public static void SyncIdentityContact(IIdentityUserContactProjection profile, IdentityUser identityUser)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(identityUser);

        profile.IdentityEmail = NormalizeEmail(identityUser.Email);
        profile.IdentityPhoneNumber = NormalizeOptional(identityUser.PhoneNumber);
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values
            .Select(NormalizeOptional)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
