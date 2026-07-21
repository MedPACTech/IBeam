using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Invites;

public sealed class DefaultTenantInviteUrlBuilder : ITenantInviteUrlBuilder
{
    public string BuildInviteUrl(TenantInviteRecord invite, string inviteToken)
    {
        var baseUrl = string.IsNullOrWhiteSpace(invite.RedirectUrl)
            ? "https://localhost:3000/invites/accept"
            : invite.RedirectUrl.Trim();

        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}inviteToken={Uri.EscapeDataString(inviteToken)}";
    }
}

public sealed class DefaultTenantInviteMessageFactory : ITenantInviteMessageFactory
{
    public IdentitySenderMessage CreateMessage(TenantInviteRecord invite, string inviteToken, string inviteUrl)
    {
        var metadata = new Dictionary<string, object>
        {
            ["inviteId"] = invite.InviteId.ToString("D"),
            ["tenantId"] = invite.TenantId.ToString("D"),
            ["inviteUrl"] = inviteUrl,
            ["inviteToken"] = inviteToken
        };

        if (!string.IsNullOrWhiteSpace(invite.CorrelationId))
            metadata["correlationId"] = invite.CorrelationId!;
        if (!string.IsNullOrWhiteSpace(invite.CausationId))
            metadata["causationId"] = invite.CausationId!;

        foreach (var kv in invite.Metadata ?? new Dictionary<string, string>())
            metadata[kv.Key] = kv.Value;

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
            Name = invite.ProfileHints?.DisplayName,
            Metadata = metadata
        };
    }
}
