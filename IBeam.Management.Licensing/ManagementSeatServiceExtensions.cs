using IBeam.Licensing;
using IBeam.Licensing.Services;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IBeam.Management.Licensing;

public sealed class ManagementSeatOptions
{
    public bool Enabled { get; set; }
    public bool AllowVolatileDevelopmentStore { get; set; }
}
public static class ManagementSeatServiceExtensions
{
    public static IReadOnlyList<ManagementOperationDescriptor> SeatDescriptors { get; } = Array.AsReadOnly(new[] {
        new ManagementOperationDescriptor(ManagementSeatOperations.Read, "GET", "/api/management-operations/tenants/{tenantId:guid}/licenses/{licenseId:guid}/seats",
            new(typeof(ManagementSeatReadRequest), "management.seats.read.request"), new(typeof(ManagementSeatStatus), "management.seats.status.result"),
            typeof(IManagementOperationHandler<ManagementSeatReadRequest, ManagementSeatStatus>), [ManagementSeatOperations.Read], 0, ManagementSeatOperations.Read, MaximumResultBytes: 262144),
        new ManagementOperationDescriptor(ManagementSeatOperations.Assign, "POST", "/api/management-operations/tenants/{tenantId:guid}/licenses/{licenseId:guid}/seats/assign",
            new(typeof(ManagementSeatAssignRequest), "management.seats.assign.request"), new(typeof(ManagementSeatTransitionResult), "management.seats.transition.result"),
            typeof(IManagementOperationHandler<ManagementSeatAssignRequest, ManagementSeatTransitionResult>), [ManagementSeatOperations.Assign, ManagementSeatOperations.Read], 1, ManagementSeatOperations.Assign),
        new ManagementOperationDescriptor(ManagementSeatOperations.Unassign, "POST", "/api/management-operations/tenants/{tenantId:guid}/licenses/{licenseId:guid}/seats/unassign",
            new(typeof(ManagementSeatUnassignRequest), "management.seats.unassign.request"), new(typeof(ManagementSeatTransitionResult), "management.seats.transition.result"),
            typeof(IManagementOperationHandler<ManagementSeatUnassignRequest, ManagementSeatTransitionResult>), [ManagementSeatOperations.Unassign, ManagementSeatOperations.Read], 1, ManagementSeatOperations.Unassign)
    }.Select(x => x with { RequiredGrants = Array.AsReadOnly(x.RequiredGrants.ToArray()) }).ToArray());
    public static IServiceCollection AddIBeamManagementSeatsTarget(this IServiceCollection services, Action<ManagementSeatOptions> configure)
    {
        var options = new ManagementSeatOptions(); configure(options);
        if (!options.Enabled) return services;
        if (services.Any(x => x.ServiceType == typeof(Registration))) throw new InvalidOperationException("Management seats are already registered.");
        var allowVolatile = options.AllowVolatileDevelopmentStore;
        services.AddSingleton(new Registration(allowVolatile)); services.AddIBeamManagementServices(); services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ITenantLicenseSeatAdministrationService>(p => new TenantLicenseSeatAdministrationService(RequireStore(p, allowVolatile),
            p.GetRequiredService<ILicenseSeatSubjectPolicy>(), p.GetRequiredService<IServiceOperationExecutor>(), p.GetRequiredService<TimeProvider>()));
        services.TryAddScoped<ManagementSeatTargetService>();
        services.AddIBeamManagementOperation<ManagementSeatReadRequest, ManagementSeatStatus, ManagementSeatReadHandler>(SeatDescriptors[0]);
        services.AddIBeamManagementOperation<ManagementSeatAssignRequest, ManagementSeatTransitionResult, ManagementSeatAssignHandler>(SeatDescriptors[1]);
        services.AddIBeamManagementOperation<ManagementSeatUnassignRequest, ManagementSeatTransitionResult, ManagementSeatUnassignHandler>(SeatDescriptors[2]);
        services.AddHostedService<Validate>(); return services;
    }
    public static IServiceCollection AddIBeamManagementSeatsPortal(this IServiceCollection services)
    {
        if (services.Any(x => x.ServiceType == typeof(PortalRegistration))) return services;
        services.AddSingleton<PortalRegistration>(); services.AddIBeamManagementServices();
        services.AddIBeamManagementOperationContract<ManagementSeatReadRequest, ManagementSeatStatus>(SeatDescriptors[0]);
        services.AddIBeamManagementOperationContract<ManagementSeatAssignRequest, ManagementSeatTransitionResult>(SeatDescriptors[1]);
        services.AddIBeamManagementOperationContract<ManagementSeatUnassignRequest, ManagementSeatTransitionResult>(SeatDescriptors[2]);
        services.TryAddScoped<IManagementSeatPortalService, ManagementSeatPortalService>(); return services;
    }
    private sealed record Registration(bool AllowVolatile);
    private sealed class PortalRegistration;
    private static ILicenseSeatConditionalStore RequireStore(IServiceProvider provider, bool allowVolatile)
    {
        var environment = provider.GetService<IHostEnvironment>()?.EnvironmentName;
        if (provider.GetRequiredService<ILicensingStore>() is not ILicenseSeatConditionalStore { SeatProtocolVersion: 1, NativeSeatWritersParticipate: true } store ||
            !store.DurableSeatReceipts && !(allowVolatile && environment is "Development" or "Testing"))
            throw new InvalidOperationException("Management seats require durable conditional native storage; volatile fixtures are explicit Development/Testing only.");
        return store;
    }
    private sealed class Validate(IServiceScopeFactory scopes, Registration configuration) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope(); var p = scope.ServiceProvider;
            _ = RequireStore(p, configuration.AllowVolatile); _ = p.GetRequiredService<ILicenseSeatSubjectPolicy>();
            if (p.GetRequiredService<IManagementOperationAccessService>().ProtocolVersion != 1 ||
                p.GetRequiredService<ILicenseSeatAssignmentService>().SeatAdministrationProtocolVersion != 1)
                throw new InvalidOperationException("Compatible live Management access and native conditional seat administration are required.");
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
