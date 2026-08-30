using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

/// <summary>
/// Optional host-app hook that supplies the greeting name for OTP sign-in messages.
/// Identity records carry no person names, so the app resolves one from its own
/// profile data (for example Users.FirstName). Return null when no name is known;
/// the message then falls back to a neutral greeting ("Hi there,").
/// </summary>
public interface IOtpRecipientNameProvider
{
    Task<string?> GetDisplayNameAsync(IdentityUser user, Guid? tenantId, CancellationToken ct = default);
}
