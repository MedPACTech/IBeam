using System.Reflection;
using System.Security.Claims;
using IBeam.Identity.Api;
using IBeam.Identity.Api.Controllers;
using IBeam.Identity.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class MembershipEventActorCaptureTests
{
    [TestMethod]
    [DataRow("customer")] [DataRow("credential")] [DataRow("service")] [DataRow("ambiguous")] [DataRow("anonymous")]
    public async Task OnlyAuthenticatedNativeHumanIdsBecomeCustomerMetadataAndBodiesCannotChooseTheActor(string kind)
    {
        var user = Guid.NewGuid(); var claims = new List<Claim> { new("uid", user.ToString("D")), new("sub", user.ToString("D")) };
        if (kind == "credential") claims.Add(new("api_credential_id", user.ToString("D")));
        if (kind == "service") claims.Add(new("principal_type", "service"));
        if (kind == "ambiguous") claims.Add(new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")));
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity(claims, kind == "anonymous" ? null : "native-trusted")) };
        var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = typeof(AuthController).GetTypeInfo() };
        var action = new ActionContext(http, new RouteData(), descriptor, new ModelStateDictionary()); var controller = new object();
        var executing = new ActionExecutingContext(action, [], new Dictionary<string, object?> { ["actorUserId"] = "PRIVATE-FORGED-ACTOR" }, controller);
        IdentityMembershipEventActor? captured = null;
        await new IdentityMembershipEventActorFilter().OnActionExecutionAsync(executing, () => {
            captured = IdentityMembershipEventActorContext.Current; return Task.FromResult(new ActionExecutedContext(action, [], controller));
        });
        Assert.IsNotNull(captured); Assert.AreEqual(kind == "customer" ? IdentityMembershipActorKind.Customer : kind is "credential" or "service" ? IdentityMembershipActorKind.System : IdentityMembershipActorKind.Unknown, captured.Kind);
        Assert.AreEqual(kind == "customer" ? user : (Guid?)null, captured.UserId);
        Assert.IsNull(captured.ManagementApplicationId); Assert.IsNull(captured.ManagementOrganizationId);
        Assert.AreEqual(IdentityMembershipActorKind.Unknown, IdentityMembershipEventActorContext.Current.Kind);
    }
    [TestMethod]
    public async Task ATrustedOuterSupportScopeIsPreservedAndRestoredOnAnActionFailure()
    {
        var support = new IdentityMembershipEventActor(IdentityMembershipActorKind.Support, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = typeof(AuthController).GetTypeInfo() };
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor, new ModelStateDictionary());
        using (IdentityMembershipEventActorContext.Push(support)) {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new IdentityMembershipEventActorFilter().OnActionExecutionAsync(new(action, [], new Dictionary<string, object?>(), new object()), () => {
                Assert.AreEqual(support, IdentityMembershipEventActorContext.Current); throw new InvalidOperationException("synthetic action failure");
            }));
            Assert.AreEqual(support, IdentityMembershipEventActorContext.Current);
        }
        Assert.AreEqual(IdentityMembershipActorKind.Unknown, IdentityMembershipEventActorContext.Current.Kind);
    }
}
