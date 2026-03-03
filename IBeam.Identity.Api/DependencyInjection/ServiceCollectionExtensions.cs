using System.Text;
using IBeam.Communications.Abstractions;
using IBeam.Communications.Email.AzureCommunications;
using IBeam.Communications.Sms.AzureCommunications;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using IBeam.Identity.Api.Controllers;
using IBeam.Identity.Repositories.AzureTable.Extensions;
using IBeam.Identity.Services;
using IBeam.Identity.Services.Otp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Identity.Api.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamIdentityApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIBeamCommunications(configuration);
        services.AddIBeamIdentityAzureTable(configuration);
        services.AddIBeamIdentityServices(configuration);

        services.AddIBeamCommunicationsSmsAzure(configuration);
        services.AddIBeamAzureCommunicationsEmail(configuration);
        services.AddScoped<IIdentityCommunicationSender, IdentityCommunicationAdapter>();

        services.AddIBeamIdentityAuthOtpService();
        services.AddIBeamIdentityAuthPasswordService();
        services.AddIBeamIdentityAuthOAuthService();

        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddDataProtection();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        jwt.Validate();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds)
                };
            });

        services.AddAuthorization();

        return services;
    }

    public static IMvcBuilder AddIBeamIdentityApiControllers(this IServiceCollection services)
    {
        return services
            .AddControllers()
            .ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Add(new AssemblyPart(typeof(AuthController).Assembly));
            });
    }
}
