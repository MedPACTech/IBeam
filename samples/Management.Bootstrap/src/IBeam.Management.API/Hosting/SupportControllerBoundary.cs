using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace IBeam.Management.API.Hosting;

/// <summary>Native customer API is referenced for the separate --customer-target mode only.
/// Prevent MVC's transitive assembly discovery from exposing customer routes inside support Identity.</summary>
internal sealed class SupportControllerBoundary : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts,ControllerFeature feature)
    {
        var customerAssembly=typeof(IBeam.Identity.Api.Controllers.AuthController).Assembly;
        foreach(var controller in feature.Controllers.Where(x=>x.Assembly==customerAssembly).ToArray())feature.Controllers.Remove(controller);
    }
}

internal sealed class CustomerControllerBoundary : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts,ControllerFeature feature)
    {
        var supportIdentity=typeof(IBeam.Management.Identity.Api.ManagementAuthenticationController).Assembly;
        var portal=typeof(IBeam.Management.Api.ManagementCustomerIdentityController).Assembly;
        foreach(var controller in feature.Controllers.Where(x=>x.Assembly==supportIdentity||x.Assembly==portal).ToArray())feature.Controllers.Remove(controller);
    }
}
