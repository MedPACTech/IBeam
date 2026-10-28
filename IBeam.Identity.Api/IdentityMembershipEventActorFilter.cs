using System.Security.Claims;
using IBeam.Identity.Events;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IBeam.Identity.Api;

/// <summary>Copies only already-authenticated native actor IDs into a trusted metadata scope; changes no authorization.</summary>
public sealed class IdentityMembershipEventActorFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action || action.ControllerTypeInfo.Assembly != typeof(IdentityMembershipEventActorFilter).Assembly ||
            IdentityMembershipEventActorContext.Current.Kind != IdentityMembershipActorKind.Unknown) { await next(); return; }
        var identities = context.HttpContext.User.Identities.Where(x => x.IsAuthenticated).ToArray();
        var actor = new IdentityMembershipEventActor(IdentityMembershipActorKind.Unknown, null, null, null, null);
        if (identities.Length == 1) {
            var identity = identities[0];
            if (identity.HasClaim(x => x.Type is "api_credential_id" or "agent_user_id" or "api_subject_type" || x.Type == "principal_type" && x.Value != "user"))
                actor = actor with { Kind = IdentityMembershipActorKind.System };
            else {
                var ids = identity.Claims.Where(x => x.Type is "uid" or "sub" || x.Type == ClaimTypes.NameIdentifier).Select(x => x.Value).Distinct(StringComparer.Ordinal).ToArray();
                if (ids.Length == 1 && Guid.TryParseExact(ids[0], "D", out var user) && user != Guid.Empty) actor = actor with { Kind = IdentityMembershipActorKind.Customer, UserId = user };
            }
        }
        using var capture = IdentityMembershipEventActorContext.Push(actor); await next();
    }
}
