using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Credits.Services;

public static class CreditServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamCreditServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIBeamServicePolicies();
        services.AddIBeamServiceAuditing(configuration);

        services.TryAddSingleton<ICreditReservationStore, InMemoryCreditStore>();
        services.TryAddSingleton<ICreditLedgerStore>(provider => provider.GetRequiredService<ICreditReservationStore>());
        services.TryAddScoped<CreditReservationService>();
        services.TryAddScoped<ICreditReservationService>(provider => provider.GetRequiredService<CreditReservationService>());
        services.TryAddScoped<ICreditUsageRecorder>(provider => provider.GetRequiredService<CreditReservationService>());
        services.TryAddScoped<ICreditPolicyService, CreditPolicyService>();
        return services;
    }
}
