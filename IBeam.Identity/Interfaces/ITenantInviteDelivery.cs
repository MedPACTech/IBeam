using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface ITenantInviteUrlBuilder
{
    string BuildInviteUrl(TenantInviteRecord invite, string inviteToken);
}

public interface ITenantInviteMessageFactory
{
    IdentitySenderMessage CreateMessage(TenantInviteRecord invite, string inviteToken, string inviteUrl);
}
