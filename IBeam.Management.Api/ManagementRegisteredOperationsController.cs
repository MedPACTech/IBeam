using IBeam.Api.Controllers;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace IBeam.Management.Api;

[NonController, Authorize(Policy = ManagementApiPolicies.Access), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}/operations")]
public sealed class ManagementRegisteredOperationsController(IManagementRegisteredOperationAvailabilityService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(Guid applicationId, CancellationToken ct) => OkResponse(await service.GetAsync(applicationId, ct));
}
public static class ManagementRegisteredOperationsApiExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementRegisteredOperationsApi(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementApi(configuration); services.AddIBeamManagementRegisteredOperationsPortal(configuration);
        services.TryAddScoped<ManagementApplicationApiFilter>();
        services.AddControllers().ConfigureApplicationPartManager(x => x.FeatureProviders.Add(new Feature())); return services;
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { var type = typeof(ManagementRegisteredOperationsController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); }
    }
}
