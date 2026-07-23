using System.Security.Claims;
using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IApiCredentialPrincipalFactory
{
    ClaimsPrincipal CreatePrincipal(ApiCredentialRecord credential, AgentUserInfo? agentUser = null);
}
