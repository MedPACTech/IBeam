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
public sealed class ManagementPortalTenantPurgeController(IManagementTenantPurgePortalService service) : ApiControllerBase
{
    [HttpPost(ManagementTenantPurgeRoutes.PortalPlan)]
    public async Task<IActionResult> Plan(Guid applicationId, Guid tenantId, [FromBody] ManagementTenantPurgePlanAction request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        var result = await service.PlanAsync(applicationId, tenantId, new("plan", request.PolicyKey, version, null, null), key, ct);
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Outcome == "unknown" ? 202 : 200; return response;
    }
    [HttpPost(ManagementTenantPurgeRoutes.PortalConfirm)]
    public async Task<IActionResult> Confirm(Guid applicationId, Guid tenantId, [FromBody] ManagementTenantPurgeConfirmAction request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        var result = await service.ConfirmAsync(applicationId, tenantId, new("confirm", request.PolicyKey, version, request.PlanId, request.ConfirmationBinding), key, ct);
        Response.Headers.Location = Url.Action(nameof(Find), new { applicationId, tenantId, jobId = result.OperationId });
        var response = (ObjectResult)OkResponse(result); response.StatusCode = 202; return response;
    }
    [HttpGet(ManagementTenantPurgeRoutes.PortalStatus)]
    public async Task<IActionResult> Find(Guid applicationId, Guid tenantId, Guid jobId, CancellationToken ct)
    {
        var result = await service.FindAsync(applicationId, tenantId, jobId, ct);
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Outcome == "unknown" || result.Job?.State == "unknown" ? 202 : 200; return response;
    }
}
public static class ManagementPortalTenantPurgeApiExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementPortalTenantPurgeApi(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementApi(configuration); services.AddIBeamManagementTenantPurgePortal();
        services.TryAddScoped<ManagementApplicationApiFilter>(); services.AddHostedService<Validate>();
        services.AddControllers().ConfigureApplicationPartManager(x => x.FeatureProviders.Add(new Feature())); return services;
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        { var type = typeof(ManagementPortalTenantPurgeController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validate(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementTenantPurgePortalService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
