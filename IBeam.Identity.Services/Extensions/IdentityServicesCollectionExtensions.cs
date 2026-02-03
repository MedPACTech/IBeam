using IBeam.Identity.Core.Auth.Interfaces;
using IBeam.Identity.Core.Options;
using IBeam.Identity.Storage.AzureTable.Extensions;
using IBeam.Identity.Storage.AzureTable.Services;
using IBeam.Identity.Storage.EntityFramework.Extensions;
using IBeam.Identity.Storage.EntityFramework.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Identity.Services.Extensions;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind provider selection + JWT options
        services.AddOptions<IdentityStoreOptions>()
            .Bind(configuration.GetSection("Identity"))
            .ValidateDataAnnotations();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"));

        // Decide which storage provider to use
        var storeOptions = configuration.GetSection("Identity").Get<IdentityStoreOptions>()
                          ?? new IdentityStoreOptions();

        switch (storeOptions.Store)
        {
            case IdentityStoreType.AzureTable:
                services.AddIBeamIdentityAzureTableStores(configuration);
                services.AddScoped<IAuthService, AzureTableAuthService>();
                break;

            case IdentityStoreType.EntityFramework:
                services.AddIBeamIdentityEntityFrameworkStores(configuration);
                services.AddScoped<IAuthService, EntityFrameworkAuthService>();
                break;
            default:
                throw new NotSupportedException($"Unsupported Identity store: {storeOptions.Store}");
        }

        // Register framework services (implementations we build next)
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        // OTP comes next — for now register placeholder
        // services.AddScoped<IOtpService, OtpService>();

        return services;
    }
}
