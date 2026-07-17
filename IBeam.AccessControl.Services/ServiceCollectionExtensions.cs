using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IBeam.Services.Abstractions;

namespace IBeam.AccessControl.Services;

public static class AccessControlServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAccessControlServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIBeamServicePolicies();
        services.AddIBeamServiceAuditing(configuration);

        services.AddOptions<AccessControlOptions>()
            .Bind(configuration.GetSection(AccessControlOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.TryAddSingleton<IResourceAccessStore, InMemoryResourceAccessStore>();
        services.TryAddSingleton<IPermissionRoleMapStore, InMemoryPermissionRoleMapStore>();
        services.TryAddScoped<IResourceAccessHierarchyResolver, NoOpResourceAccessHierarchyResolver>();
        services.TryAddScoped<IResourceAccessService, ResourceAccessService>();
        services.TryAddScoped<IResourceAccessAuthorizer, ResourceAccessAuthorizer>();
        services.TryAddScoped<IPermissionRoleMapService, PermissionRoleMapService>();
        services.TryAddScoped<IPermissionRoleAuthorizer, PermissionRoleAuthorizer>();
        services.AddIBeamServiceOperationAuthorization(configuration);

        return services;
    }

    public static IServiceCollection AddIBeamServiceOperationAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ServiceOperationAuthorizationOptions>()
            .Bind(configuration.GetSection(ServiceOperationAuthorizationOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.TryAddSingleton<IServiceOperationPermissionStore, InMemoryServiceOperationPermissionStore>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IServiceOperationPermissionRuleProvider, ConfigServiceOperationPermissionRuleProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IServiceOperationPermissionRuleProvider, StoreServiceOperationPermissionRuleProvider>());
        services.TryAddScoped<IServiceOperationAuthorizer, ServiceOperationAuthorizer>();

        return services;
    }

    public static IServiceCollection AddIBeamServiceOperationPermissionManagement(this IServiceCollection services)
    {
        services.TryAddScoped<IServiceOperationPermissionService, ServiceOperationPermissionService>();
        return services;
    }
}
