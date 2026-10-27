using System.Security.Cryptography;

namespace IBeam.Management.Identity.Target.Services;

/// <summary>Opaque invitation tokens safe for URL path segments, with 256 bits of entropy.</summary>
internal static class ManagementInviteToken
{
    internal static string Create() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // Recovery journals written by 2.16.0 contain the original 44-character padded token.
    // Accept those without normalizing the raw string that was hashed and sent.
    internal static bool IsRecoveryToken(string? token) => token is { Length: 44 } ||
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
