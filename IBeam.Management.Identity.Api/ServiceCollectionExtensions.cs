using IBeam.Identity.Options;
using IBeam.Identity.Services.Tokens;
using IBeam.Management.Api;
using IBeam.Management.Identity.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Management.Identity.Api;

public static class ManagementIdentityAuthenticationDefaults
{
    public const string Scheme = "IBeam.Management.Identity";
}

public static class ManagementIdentityApiServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamManagementIdentityApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementIdentityServices(configuration);
        services.AddIBeamManagementApi(configuration, o => o.AuthenticationScheme = ManagementIdentityAuthenticationDefaults.Scheme);
        services.AddAuthentication().AddJwtBearer(ManagementIdentityAuthenticationDefaults.Scheme, _ => { });
        services.AddOptions<JwtBearerOptions>(ManagementIdentityAuthenticationDefaults.Scheme)
            .Configure<IOptions<JwtOptions>, IJwtSigningKeyProvider>((bearer, jwt, keys) =>
            {
                bearer.MapInboundClaims = false;
                bearer.IncludeErrorDetails = false;
                bearer.SaveToken = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true, ValidAudience = jwt.Value.Audience,
                    ValidateLifetime = true, RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = [keys.SigningCredentials.Algorithm],
                    IssuerSigningKeyResolver = (_, _, keyId, _) => keys.GetValidationKeys()
                        .Where(k => string.IsNullOrWhiteSpace(keyId) || k.KeyId == keyId),
                    NameClaimType = "uid", RoleClaimType = "role"
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        try
                        {
                            var sessions = context.HttpContext.RequestServices.GetRequiredService<ManagementIdentitySessionService>();
                            var member = await sessions.ValidateAccessAsync(context.Principal!, context.HttpContext.RequestAborted);
                            ManagementIdentitySessionService.RefreshPrincipal(context.Principal!, member,
                                context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<ManagementIdentityOptions>>().CurrentValue,
                                context.HttpContext.RequestServices.GetRequiredService<global::IBeam.Management.Operations.ManagementOperationRegistry>());
                        }
                        catch (ManagementAccessDeniedException) { context.Fail("Management session is not active."); }
                    }
                };
            });
        services.AddHostedService<ManagementIdentityRuntimeValidator>();
        services.AddScoped<ManagementIdentityExceptionFilter>();
        services.AddControllers().AddApplicationPart(typeof(ManagementAuthenticationController).Assembly);
        return services;
    }
}
