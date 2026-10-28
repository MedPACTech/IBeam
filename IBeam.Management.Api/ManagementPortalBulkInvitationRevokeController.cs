using System.Reflection;
using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services;
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
public sealed class ManagementPortalBulkInvitationRevokeController(IManagementPortalBulkInvitationRevokeService service) : ApiControllerBase
{
    [HttpPost(ManagementBulkInvitationRevokeOperation.PortalCommand), RequestSizeLimit(32768)]
    public async Task<IActionResult> Execute(Guid applicationId, Guid tenantId, Guid batchId, [FromBody] ManagementBulkInvitationRevokeRequest request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        var result = await service.ExecuteAsync(applicationId, tenantId, batchId, request, key, version, ct);
        Response.Headers.Location = Url.Action(nameof(Receipt), new { applicationId, tenantId, batchId, operationId = result.OperationId });
        var response = (ObjectResult)OkResponse(result); response.StatusCode = 202; return response;
    }
    [HttpGet(ManagementBulkInvitationRevokeOperation.PortalReceipt)]
    public async Task<IActionResult> Receipt(Guid applicationId, Guid tenantId, Guid batchId, Guid operationId, CancellationToken ct)
    {
        var result = await service.ReadReceiptAsync(applicationId, tenantId, batchId, operationId, ct);
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Outcome == "unknown" || result.State == "unknown" ? 202 : 200; return response;
    }
}
public static class ManagementPortalBulkInvitationRevokeApiExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementPortalBulkInvitationRevokeApi(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementApi(configuration); services.AddIBeamManagementPortalBulkInvitationRevoke();
        services.TryAddScoped<ManagementApplicationApiFilter>(); services.AddHostedService<Validate>();
        services.AddControllers().ConfigureApplicationPartManager(x => x.FeatureProviders.Add(new Feature())); return services;
    }
    private sealed class Feature : IApplicationFeatureProvider<ControllerFeature>
    { public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) { var type = typeof(ManagementPortalBulkInvitationRevokeController).GetTypeInfo(); if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type); } }
    private sealed class Validate(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); _ = scope.ServiceProvider.GetRequiredService<IManagementPortalBulkInvitationRevokeService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
