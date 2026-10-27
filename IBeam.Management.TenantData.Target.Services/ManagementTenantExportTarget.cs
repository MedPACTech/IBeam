using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.TenantData;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Management.TenantData.Target.Services;

public sealed class ManagementTenantExportOptions
{
    public bool Enabled { get; set; }
    public bool AllowVolatileDevelopmentJobs { get; set; }
    public string[] PolicyKeys { get; set; } = [];
}
public sealed class ManagementTenantExportTarget(IManagementOperationAccessService access, IManagementTenantExportJobService jobs,
    IManagementTenantExportHandler handler, IManagementTenantExportPolicy policy, IOptions<ManagementDiagnosticsOptions> deployment,
    IOptions<ManagementTenantExportOptions> options, IServiceOperationExecutor operations)
    : IManagementOperationHandler<ManagementTenantExportRequest, ManagementTenantExportResult>, IManagementOperationReceiptHandler<ManagementTenantExportResult>
{
    public Task<ManagementTenantExportResult> ExecuteAsync(ManagementOperationContext context, ManagementTenantExportRequest request, CancellationToken ct = default)
        => Run(context, async token =>
        {
            if (request is null || !ManagementTenantExportProtocol.Key(request.PolicyKey) || !options.Value.PolicyKeys.Contains(request.PolicyKey, StringComparer.Ordinal) ||
                request.ExpectedVersion is not { Length: > 0 and <= 256 } || request.ExpectedVersion == "*" || request.ExpectedVersion.Any(char.IsControl) || context.ResourceVersion != request.ExpectedVersion)
                throw new ManagementOperationInvalidException();
            var definition = ManagementTenantExportProtocol.Definition(handler.Describe());
            var execution = new ManagementTenantExportExecution(context, deployment.Value.ApplicationKey, deployment.Value.Environment, context.OperationId, request.PolicyKey, definition.InventoryVersion);
            // The standard durable service checks immutable prior intent before a fresh
            // native version precondition; replay must not reapply or fail an old command.
            if (jobs is not ManagementTenantExportJobService) await policy.RequireAsync(execution, request.ExpectedVersion, token);
            await access.RequireAsync(ManagementTenantDataOperations.Export, context, token);
            return await jobs.SubmitAsync(execution, request, token);
        }, ct);

    public Task<ManagementTenantExportResult> ReadReceiptAsync(ManagementOperationContext context, CancellationToken ct = default)
        => Run(context, token => jobs.FindAsync(new(context, deployment.Value.ApplicationKey, deployment.Value.Environment, context.OperationId, "", 0), token), ct, lookup: true);

    private async Task<ManagementTenantExportResult> Run(ManagementOperationContext scope, Func<CancellationToken, Task<ManagementTenantExportResult>> action, CancellationToken ct, bool lookup = false)
    {
        try { return await operations.ExecuteAsync(this, async token =>
        {
          try {
            if (access.ProtocolVersion != 1 || scope.OrganizationId == Guid.Empty || scope.ApplicationId == Guid.Empty || scope.RealmId == Guid.Empty || scope.ActorUserId == Guid.Empty ||
                scope.TenantId == Guid.Empty || scope.ResourceId != scope.TenantId || scope.OperationId == Guid.Empty) throw new ManagementAccessDeniedException();
            await access.RequireAsync(ManagementTenantDataOperations.Export, scope, token);
            var result = await action(token);
            await access.RequireAsync(ManagementTenantDataOperations.Export, scope, token);
            var safe = jobs is ManagementTenantExportJobService ? result : ManagementTenantExportProtocol.Result(result, scope.TenantId, scope.OperationId, handler.Describe());
            if (safe.Outcome == "unknown") throw new UnknownResult(safe);
            return safe;
          }
          catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationInvalidException or ManagementOperationPreconditionException or
              ManagementOperationConflictException or ManagementOperationUnavailableException or UnknownResult) && !(ex is OperationCanceledException && token.IsCancellationRequested))
          { throw new ManagementOperationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = lookup ? "management.operations.lookup" : ManagementTenantDataOperations.Export,
            AuditAction = lookup ? "management.operations.lookup" : ManagementTenantDataOperations.Export,
            EntityId = scope.TenantId, EntityName = "ManagementTenantExportJob", PermissionEnabled = false, AuditEnabled = true }, ct);
        }
        catch (UnknownResult unknown) { return unknown.Result; }
    }
    private sealed class UnknownResult(ManagementTenantExportResult result) : Exception("The export outcome is unknown.")
    { public ManagementTenantExportResult Result { get; } = result; }
}

public static class ManagementTenantExportServiceExtensions
{
    public static ManagementOperationDescriptor Descriptor { get; } = new(ManagementTenantDataOperations.Export, "POST", ManagementTenantExportRoutes.Submit,
        new(typeof(ManagementTenantExportRequest), "management.tenant-export.submit.request"), new(typeof(ManagementTenantExportResult), "management.tenant-export.job.result"),
        typeof(IManagementOperationHandler<ManagementTenantExportRequest, ManagementTenantExportResult>), Array.AsReadOnly(new[] { ManagementTenantDataOperations.Export }), 1, ManagementTenantDataOperations.Export);

    public static IServiceCollection AddIBeamManagementTenantExportTarget(this IServiceCollection services, Action<ManagementTenantExportOptions> configure)
    {
        var configured = new ManagementTenantExportOptions(); configure(configured);
        if (!configured.Enabled) return services;
        if (services.Any(x => x.ServiceType == typeof(Registration))) throw new InvalidOperationException("Tenant export is already registered.");
        services.AddSingleton<Registration>();
        services.AddIBeamManagementServices();
        var keys = configured.PolicyKeys?.ToArray();
        services.AddOptions<ManagementTenantExportOptions>().Configure(o => { o.Enabled = true; o.AllowVolatileDevelopmentJobs = configured.AllowVolatileDevelopmentJobs; o.PolicyKeys = keys ?? []; })
            .Validate(o => o.PolicyKeys is { Length: > 0 and <= 32 } && o.PolicyKeys.All(ManagementTenantExportProtocol.Key) && o.PolicyKeys.Distinct(StringComparer.Ordinal).Count() == o.PolicyKeys.Length,
                "Explicit bounded target-owned policy keys are required.").ValidateOnStart();
        services.AddIBeamManagementOperation<ManagementTenantExportRequest, ManagementTenantExportResult, ManagementTenantExportTarget>(Descriptor);
        services.AddHostedService<Validate>(); return services;
    }
    private sealed class Registration;
    private sealed class Validate(IServiceScopeFactory scopes, IOptions<ManagementTenantExportOptions> options, IHostEnvironment environment) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope(); var p = scope.ServiceProvider;
            var handler = p.GetRequiredService<IManagementTenantExportHandler>();
            _ = ManagementTenantExportProtocol.Definition(handler.Describe());
            var jobs = p.GetRequiredService<IManagementTenantExportJobService>();
            if (handler.ProtocolVersion != 1 || jobs.ProtocolVersion != 1 ||
                !jobs.Durable && !(options.Value.AllowVolatileDevelopmentJobs && environment.EnvironmentName is "Development" or "Testing") ||
                p.GetRequiredService<IManagementTenantExportDelivery>().ProtocolVersion != 1 ||
                p.GetRequiredService<IManagementTenantExportCustomerAuthorization>().ProtocolVersion != 1 ||
                p.GetRequiredService<IManagementTenantExportPolicy>().ProtocolVersion != 1 || p.GetRequiredService<IManagementOperationAccessService>().ProtocolVersion != 1)
                throw new InvalidOperationException("Export requires a usable handler, customer-owned delivery/authorization/policy, compatible live access and durable scoped jobs.");
            _ = p.GetRequiredService<IManagementOperationHandler<ManagementTenantExportRequest, ManagementTenantExportResult>>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
