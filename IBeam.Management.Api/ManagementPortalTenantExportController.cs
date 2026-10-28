using System.Reflection;
using IBeam.Api.Controllers;
using IBeam.Management.Services;
using IBeam.Management.TenantData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IBeam.Management.Api;

[NonController, Authorize(Policy = ManagementApiPolicies.Access), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route(ManagementTenantExportRoutes.PortalSubmit)]
public sealed class ManagementPortalTenantExportController(IManagementTenantExportPortalService service) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit(Guid applicationId, Guid tenantId, [FromBody] ManagementTenantExportAction request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        var result = await service.SubmitAsync(applicationId, tenantId, new(request.PolicyKey, version), key, ct);
        Response.Headers.Location = Url.Action(nameof(Find), new { applicationId, tenantId, jobId = result.JobId });
        var response = (ObjectResult)OkResponse(result); response.StatusCode = 202; return response;
    }
    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Find(Guid applicationId, Guid tenantId, Guid jobId, CancellationToken ct)
    {
        var result = await service.FindAsync(applicationId, tenantId, jobId, ct);
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Outcome == "unknown" ? 202 : 200; return response;
    }
}
public static class ManagementPortalTenantExportApiExtensions
{
    private sealed class Registration;
    /// <summary>Explicit human HTTP opt-in. The shared registered client supplies live authorization/signing; no native exporter is installed in the portal.</summary>
    public static IServiceCollection AddIBeamManagementPortalTenantExportApi(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementApi(configuration); services.AddIBeamManagementTenantExportPortal();
        services.TryAddScoped<ManagementApplicationApiFilter>(); services.AddHostedService<Validate>();
        services.AddControllers().ConfigureApplicationPartManager(x => x.FeatureProviders.Add(new Feature())); return services;
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { var type = typeof(ManagementPortalTenantExportController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validate(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementTenantExportPortalService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
