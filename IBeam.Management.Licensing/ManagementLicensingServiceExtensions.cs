using IBeam.Licensing;
using IBeam.Licensing.Services;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IBeam.Management.Licensing;

public sealed class ManagementLicensingOptions
{
    public bool Enabled { get; set; }
    public bool EnableRestore { get; set; }
    public bool AllowVolatileDevelopmentStore { get; set; }
}
public static class ManagementLicensingServiceExtensions
{
    public static IReadOnlyList<ManagementOperationDescriptor> LicenseStatusDescriptors { get; } = CreateDescriptors();
    private static IReadOnlyList<ManagementOperationDescriptor> CreateDescriptors() => Array.AsReadOnly(new[] {
        new ManagementOperationDescriptor(ManagementLicenseOperations.Read, "GET", "/api/management-operations/tenants/{tenantId:guid}/licenses/{licenseId:guid}",
            new(typeof(ManagementLicenseReadRequest), "management.licenses.read.request"), new(typeof(ManagementLicenseStatus), "management.licenses.status.result"),
            typeof(IManagementOperationHandler<ManagementLicenseReadRequest, ManagementLicenseStatus>), [ManagementLicenseOperations.Read], 0, ManagementLicenseOperations.Read),
        new ManagementOperationDescriptor(ManagementLicenseOperations.Suspend, "POST", "/api/management-operations/tenants/{tenantId:guid}/licenses/{licenseId:guid}/suspend",
            new(typeof(ManagementLicenseSuspendRequest), "management.licenses.suspend.request"), new(typeof(ManagementLicenseTransitionResult), "management.licenses.transition.result"),
            typeof(IManagementOperationHandler<ManagementLicenseSuspendRequest, ManagementLicenseTransitionResult>), [ManagementLicenseOperations.Suspend, ManagementLicenseOperations.Read], 1, ManagementLicenseOperations.Suspend),
        new ManagementOperationDescriptor(ManagementLicenseOperations.Restore, "POST", "/api/management-operations/tenants/{tenantId:guid}/licenses/{licenseId:guid}/restore",
            new(typeof(ManagementLicenseRestoreRequest), "management.licenses.restore.request"), new(typeof(ManagementLicenseTransitionResult), "management.licenses.transition.result"),
            typeof(IManagementOperationHandler<ManagementLicenseRestoreRequest, ManagementLicenseTransitionResult>), [ManagementLicenseOperations.Restore, ManagementLicenseOperations.Read], 1, ManagementLicenseOperations.Restore)
    }.Select(x => x with { RequiredGrants = Array.AsReadOnly(x.RequiredGrants.ToArray()) }).ToArray());
    /// <summary>Explicit optional target registration. Supply native Licensing, compatible live Management access and durable fenced storage.</summary>
    public static IServiceCollection AddIBeamManagementLicenseStatusTarget(this IServiceCollection services, Action<ManagementLicensingOptions> configure)
    {
        var options = new ManagementLicensingOptions(); configure(options);
        if (!options.Enabled) return services;
        if (services.Any(x => x.ServiceType == typeof(Registration))) throw new InvalidOperationException("Management Licensing is already registered.");
        services.AddSingleton(new Registration(options.EnableRestore, options.AllowVolatileDevelopmentStore));
        var allowVolatile = options.AllowVolatileDevelopmentStore;
        services.AddIBeamManagementServices();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ITenantLicenseStatusAdministrationService>(provider => new TenantLicenseStatusAdministrationService(
            RequireStore(provider, allowVolatile),
            provider.GetService<ILicenseStatusRestorePolicy>() ?? new DeniedRestore(),
            provider.GetRequiredService<IServiceOperationExecutor>(), provider.GetRequiredService<TimeProvider>()));
        services.TryAddScoped<ManagementLicenseTargetService>();
        services.AddIBeamManagementOperation<ManagementLicenseReadRequest, ManagementLicenseStatus, ManagementLicenseReadHandler>(LicenseStatusDescriptors[0]);
        services.AddIBeamManagementOperation<ManagementLicenseSuspendRequest, ManagementLicenseTransitionResult, ManagementLicenseSuspendHandler>(LicenseStatusDescriptors[1]);
        if (options.EnableRestore)
            services.AddIBeamManagementOperation<ManagementLicenseRestoreRequest, ManagementLicenseTransitionResult, ManagementLicenseRestoreHandler>(LicenseStatusDescriptors[2]);
        services.AddHostedService<Validate>();
        return services;
    }
    /// <summary>Portal catalog registration does not install target handlers, native Licensing or grant authority.</summary>
    public static IServiceCollection AddIBeamManagementLicensePortal(this IServiceCollection services)
    {
        if (services.Any(x => x.ServiceType == typeof(PortalRegistration))) return services;
        services.AddSingleton<PortalRegistration>();
        services.AddIBeamManagementServices();
        services.AddIBeamManagementOperationContract<ManagementLicenseReadRequest, ManagementLicenseStatus>(LicenseStatusDescriptors[0]);
        services.AddIBeamManagementOperationContract<ManagementLicenseSuspendRequest, ManagementLicenseTransitionResult>(LicenseStatusDescriptors[1]);
        services.AddIBeamManagementOperationContract<ManagementLicenseRestoreRequest, ManagementLicenseTransitionResult>(LicenseStatusDescriptors[2]);
        services.TryAddScoped<IManagementLicensePortalService, ManagementLicensePortalService>();
        return services;
    }
    private sealed record Registration(bool Restore, bool AllowVolatile);
    private sealed class PortalRegistration;
    private sealed class Validate(IServiceProvider provider, Registration configuration) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = provider.CreateScope();
            var scoped = scope.ServiceProvider;
            _ = RequireStore(scoped, configuration.AllowVolatile);
            if (scoped.GetRequiredService<IManagementOperationAccessService>().ProtocolVersion != 1 ||
                configuration.Restore && scoped.GetService<ILicenseStatusRestorePolicy>() is null)
                throw new InvalidOperationException("Compatible live Management access and an explicit restore policy are required.");
            if (scoped.GetRequiredService<ITenantLicenseService>().StatusAdministrationProtocolVersion != 1)
                throw new InvalidOperationException("The native licence service must implement conditional status administration.");
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private static ILicenseStatusConditionalStore RequireStore(IServiceProvider provider, bool allowVolatile)
    {
        var store = provider.GetRequiredService<ILicensingStore>();
        var environment = provider.GetService<IHostEnvironment>()?.EnvironmentName;
        if (store is not ILicenseStatusConditionalStore { StatusProtocolVersion: 1, NativeWritersParticipate: true } conditional ||
            !conditional.DurableStatusReceipts && !(allowVolatile && environment is "Development" or "Testing"))
            throw new InvalidOperationException("Management licence status requires durable fenced native storage; volatile stores are explicit Development/Testing fixtures only.");
        return conditional;
    }
    private sealed class DeniedRestore : ILicenseStatusRestorePolicy
    {
        public Task RequireAsync(LicenseStatusCommand command, TenantLicenseRecord current, TenantLicenseRecord candidate, CancellationToken ct = default)
            => throw new LicenseStatusConflictException();
    }
}
