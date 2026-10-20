using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services;

public static class ManagementCustomerIdentityTargetServiceExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementCustomerIdentityTargetServices(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddIBeamManagementCredentialValidation(configuration);
        services.AddOptions<ManagementCustomerIdentityTargetOptions>()
            .Bind(configuration.GetSection(ManagementCustomerIdentityTargetOptions.SectionName))
            .Validate(x => x.IdentityRealmId != Guid.Empty && x.DedicatedApplicationRealm,
                "An explicit non-empty customer Identity realm and DedicatedApplicationRealm=true are required. Shared app realms need a trusted scope adapter before support.")
            .Validate(x => x.EnabledOperations is not null && x.EnabledOperations.Length < ManagementCustomerIdentityOperations.All.Count &&
                x.EnabledOperations.All(ManagementCustomerIdentityPermissionPolicy.IsAssignable) &&
                x.EnabledOperations.Distinct(StringComparer.Ordinal).Count() == x.EnabledOperations.Length,
                "EnabledOperations must contain distinct, explicit customer operation names; wildcard/default authority is not supported.")
            .ValidateOnStart();
        services.TryAddScoped<IManagementCustomerIdentityInfoService, ManagementCustomerIdentityInfoService>();
        return services;
    }
}
