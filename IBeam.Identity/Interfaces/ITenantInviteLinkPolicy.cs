using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

/// <summary>
/// Decides where an invite's "Accept invitation" link may point (IBM-0068). Replace this in DI when an app
/// needs rules the built-in policy cannot express; the default implementation covers an allowlist, a
/// fallback URL, a fixed accept path, and a code delegate.
/// </summary>
public interface ITenantInviteLinkPolicy
{
    /// <summary>
    /// The base URL this invite's link may be built on — never the caller's redirect unless that redirect
    /// was allowed. The invite token is appended by the URL builder, not here.
    /// </summary>
    /// <exception cref="Exceptions.IdentityValidationException">
    /// When the requested redirect is not allowed and <c>OnDisallowed</c> is <c>Reject</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// When a fallback is needed and <c>DefaultAcceptUrl</c> is not configured.
    /// </exception>
    string ResolveAcceptUrl(TenantInviteRecord invite);
}

/// <summary>
/// The metadata keys the outgoing invite message and IBeam's message templates own.
/// <para>
/// Caller-supplied invite metadata used to be merged over IBeam's own values, so
/// <c>metadata: { "inviteUrl": "https://evil.example" }</c> replaced the link even when the redirect URL
/// was perfectly valid. These keys are now applied after caller metadata and always win.
/// </para>
/// </summary>
public static class TenantInviteReservedMetadataKeys
{
    /// <summary>Compared case-insensitively: the message metadata dictionary used to be case-sensitive, so <c>InviteUrl</c> sat alongside <c>inviteUrl</c> and both reached the template model.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Set by the message factory from the invite itself.
        "inviteId", "tenantId", "inviteUrl", "inviteToken", "correlationId", "causationId",
        // Rendered by the message templates. A caller value here could replace visible text or the link.
        "Link", "Code", "Subject", "Body", "Name", "Destination", "Purpose",
        "ExpiresAt", "ExpiresAtUtc", "ExpiresInMinutes"
    };

    /// <summary>The reserved keys present in caller-supplied metadata, if any. Used to reject them at create time.</summary>
    public static IReadOnlyList<string> PresentIn(IReadOnlyDictionary<string, string>? metadata) =>
        metadata is null ? [] : metadata.Keys.Where(All.Contains).ToList();
}
