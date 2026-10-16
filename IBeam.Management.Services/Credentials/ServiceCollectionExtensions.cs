using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Services.Credentials;

public static class ManagementCredentialServiceCollectionExtensions
{
    /// <summary>Runtime read-only credential validation; the host supplies IManagementApiCredentialRepository.</summary>
    public static IServiceCollection AddIBeamManagementCredentialValidation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementServices();
        services.AddOptions<ManagementDiagnosticsOptions>().Bind(configuration.GetSection(ManagementDiagnosticsOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton<ManagementCredentialSecretCodec>();
        services.TryAddScoped<IManagementCredentialValidator, ManagementCredentialValidator>();
        return services;
    }

    /// <summary>Trusted setup only, never runtime/public registration. Explicit enabling AND operator authorization are required.</summary>
    public static IServiceCollection AddIBeamManagementCredentialProvisioning(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementCredentialValidation(configuration);
        services.AddOptions<ManagementCredentialProvisioningOptions>()
            .Bind(configuration.GetSection(ManagementCredentialProvisioningOptions.SectionName))
            .Validate(o => o.MaximumLifetimeDays is > 0 and <= 365, "Maximum credential lifetime must be 1-365 days.")
            .ValidateOnStart();
        services.TryAddScoped<IManagementCredentialOperatorProvider, DeniedOperator>();
        services.TryAddScoped<IManagementCredentialProvisioningService, ManagementCredentialProvisioningService>();
        return services;
    }

    private sealed class DeniedOperator : IManagementCredentialOperatorProvider
    {
        public Task<Guid> RequireAuthorizedOperatorAsync(CancellationToken ct = default)
            => throw new ManagementAccessDeniedException();
    }
}
