using IBeam.Identity.Interfaces;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Services.Invitations;

public static class ManagementCustomerInvitationServiceExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementCustomerInvitations(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddIBeamManagementIdentityOperations(configuration);
        services.AddOptions<ManagementCustomerInvitationOptions>().Bind(configuration.GetSection(ManagementCustomerInvitationOptions.SectionName))
            .Validate(x => x.IsValid(), "Invitation lifecycle requires bounded target-owned roles and resend limits.").ValidateOnStart();
        services.TryAddScoped<IManagedTenantInviteStore>(p => p.GetRequiredService<ITenantInviteStore>() as IManagedTenantInviteStore
            ?? throw new InvalidOperationException("A conditional native invitation store is required."));
        services.TryAddScoped<IManagedTenantInviteDeliveryService>(p => p.GetRequiredService<ITenantInviteService>() as IManagedTenantInviteDeliveryService
            ?? throw new InvalidOperationException("A conditional native invitation delivery adapter is required."));
        services.TryAddScoped<IManagementCustomerInvitationService, ManagementCustomerInvitationService>();
        return services;
    }
}
