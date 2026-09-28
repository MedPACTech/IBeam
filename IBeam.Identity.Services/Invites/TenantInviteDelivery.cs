using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Invites;

/// <summary>
/// Builds the invite link from the destination an <see cref="ITenantInviteLinkPolicy"/> approved, not from
/// the caller's redirect URL (IBM-0068).
/// </summary>
/// <remarks>
/// This runs on the single delivery path shared by creating and resending an invite
/// (<c>TenantInviteService.SendInviteAsync</c>), so an invite stored with a redirect URL that is no longer
/// allowed — or was never allowed, because it predates this policy — gets a safe link when it is resent.
/// Validating only at create time would have left those records behind.
/// </remarks>
public sealed class DefaultTenantInviteUrlBuilder : ITenantInviteUrlBuilder
{
    private readonly ITenantInviteLinkPolicy _linkPolicy;

    public DefaultTenantInviteUrlBuilder(ITenantInviteLinkPolicy linkPolicy)
    {
        _linkPolicy = linkPolicy;
    }

    public string BuildInviteUrl(TenantInviteRecord invite, string inviteToken)
    {
        ArgumentNullException.ThrowIfNull(invite);

        var baseUrl = _linkPolicy.ResolveAcceptUrl(invite);
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}inviteToken={Uri.EscapeDataString(inviteToken)}";
    }
}

public sealed class DefaultTenantInviteMessageFactory : ITenantInviteMessageFactory
{
    public IdentitySenderMessage CreateMessage(TenantInviteRecord invite, string inviteToken, string inviteUrl)
    {
        ArgumentNullException.ThrowIfNull(invite);

        // Case-insensitive, and caller metadata goes in FIRST so IBeam's own values overwrite it rather
        // than the other way round (IBM-0068). Previously the system keys were set first and then the
        // caller's metadata was copied over them, so metadata: { "inviteUrl": "https://evil.example" }
        // replaced the link even when the redirect URL was valid. A case-sensitive dictionary also let
        // "InviteUrl" reach the template model alongside "inviteUrl".
        var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in invite.Metadata ?? new Dictionary<string, string>())
        {
            if (TenantInviteReservedMetadataKeys.All.Contains(kv.Key))
                continue;

            metadata[kv.Key] = kv.Value;
        }

        metadata["inviteId"] = invite.InviteId.ToString("D");
        metadata["tenantId"] = invite.TenantId.ToString("D");
        metadata["inviteUrl"] = inviteUrl;
        metadata["inviteToken"] = inviteToken;

        if (!string.IsNullOrWhiteSpace(invite.CorrelationId))
            metadata["correlationId"] = invite.CorrelationId!;
        if (!string.IsNullOrWhiteSpace(invite.CausationId))
            metadata["causationId"] = invite.CausationId!;

        return new IdentitySenderMessage
        {
            Channel = invite.DestinationType == TenantInviteDestinationTypes.Sms ? SenderChannel.Sms : SenderChannel.Email,
            Destination = invite.NormalizedDestination,
            Code = inviteToken,
            Purpose = SenderPurpose.TenantInvitation,
            TenantId = invite.TenantId,
            ExpiresAt = invite.ExpiresUtc,
            Subject = "You're invited",
            Body = $"You have been invited to join a workspace. Open this invite link: {inviteUrl}",
            Name = ComposeInviteeName(invite.ProfileHints),
            Metadata = metadata
        };
    }

    private static string? ComposeInviteeName(TenantInviteProfileHints? hints)
    {
        var name = string.Join(' ', new[] { hints?.FirstName, hints?.LastName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        return name.Length > 0 ? name : null;
    }
}
