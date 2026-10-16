using IBeam.Identity.Interfaces;
using IBeam.Identity.Repositories.AzureTable.Extensions;
using IBeam.Identity.Services;
using IBeam.Management.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Identity.Services;

public static class ManagementIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamManagementIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Provider services/options are not keyed. Two Identity realms cannot share one service collection.
        if (services.Any(x => x.ServiceType == typeof(IIdentityUserStore) ||
            x.ServiceType == typeof(ITokenService) || x.ServiceType == typeof(IIdentityTenantService)))
            throw new InvalidOperationException("Management Identity requires its own host/service collection.");
        var isolated = ManagementIdentityConfiguration.Build(configuration);
        services.AddOptions<ManagementIdentityOptions>()
            .Bind(configuration.GetSection(ManagementIdentityOptions.SectionName))
            .Validate(o => o.OrganizationId != Guid.Empty, "Management Identity OrganizationId is required.")
            .ValidateOnStart();
        services.AddIBeamIdentityAzureTable(isolated, registerSchemaHostedService: false);
        services.AddIBeamIdentityServices(isolated);
        services.AddIBeamManagementServices();
        services.AddIBeamIdentityAuthPasswordService();
        services.AddScoped<IBeam.Identity.Services.Auth.PasswordAuthService>();
        services.Replace(ServiceDescriptor.Scoped<IIdentityAuthService, ManagementPasswordAuthService>());
        services.AddScoped<ManagementIdentityMembershipService>();
        services.AddScoped<ManagementIdentitySessionService>();
        services.AddScoped<ManagementSupportAccessService>();
        services.AddScoped<IManagementIdentityAuthenticationService, ManagementIdentityAuthenticationService>();
        services.AddScoped<IClaimsEnricher, ManagementIdentityClaimsEnricher>();
        services.Replace(ServiceDescriptor.Scoped<IAuthLifecycleHook, ManagementIdentityEnrollmentHook>());
        services.TryAddScoped<IManagementIdentityBootstrapService, ManagementIdentityBootstrapService>();
        return services;
    }
}
