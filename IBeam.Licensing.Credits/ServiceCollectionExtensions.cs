using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Licensing.Credits;

public static class LicenseCreditGateServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamLicenseCreditGate(this IServiceCollection services)
    {
        services.TryAddScoped<ILicenseCreditGate, LicenseCreditGate>();
        return services;
    }
}
