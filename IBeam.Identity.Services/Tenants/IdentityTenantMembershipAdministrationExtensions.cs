using IBeam.Identity.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Identity.Services.Tenants;

public static class IdentityTenantMembershipAdministrationExtensions
{
    public static IServiceCollection AddIBeamIdentityTenantMembershipAdministration(this IServiceCollection services)
    {
        services.TryAddScoped<IIdentityTenantMembershipConditionalStore>(p => p.GetRequiredService<ITenantMembershipStore>() as IIdentityTenantMembershipConditionalStore
            ?? throw new InvalidOperationException("Conditional native tenant membership storage is required."));
        services.TryAddScoped<IIdentityTenantMembershipAdministrationService, IdentityTenantMembershipAdministrationService>();
        services.TryAddScoped<IIdentityTenantAccessValidator, IdentityTenantAccessValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsEnricher, IdentityTenantAccessClaimsEnricher>());
        return services;
    }
}
