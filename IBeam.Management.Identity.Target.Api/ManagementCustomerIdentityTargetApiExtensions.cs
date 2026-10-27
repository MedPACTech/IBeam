using System.Net;
using System.Reflection;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services;
using IBeam.Management.Services.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Identity.Target.Api;

public static class ManagementCustomerIdentityTargetApiExtensions
{
    public const string Policy = "IBeam.Management.CustomerIdentity.Target";
    public const string Scheme = "IBeam.Management.CustomerIdentity.Credential";
    /// <summary>Opt-in only. The host supplies target credential repository, deployment binding and customer realm configuration.</summary>
    public static IServiceCollection AddIBeamManagementCustomerIdentityTargetApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementCustomerIdentityTargetServices(configuration);
        services.AddOptions<ManagementCredentialNetworkOptions>().Bind(configuration.GetSection(ManagementCredentialNetworkOptions.SectionName))
            .Validate(x => x.AllowedNetworks is { Count: <= 100 } && x.AllowedNetworks.All(n => IPNetwork.TryParse(n, out _)),
                "AllowedNetworks requires at most 100 valid CIDRs.").ValidateOnStart();
        services.TryAddSingleton<ManagementCredentialNetworkPolicy>();
        services.TryAddScoped<IManagementCredentialAuthenticationService, ManagementCredentialAuthenticationService>();
        services.AddHttpContextAccessor();
        services.TryAddScoped<IManagementPrincipalProvider, TargetPrincipal>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ManagementCustomerIdentityAuthenticationHandler>(Scheme, _ => { });
        services.AddAuthorization(x => x.AddPolicy(Policy, policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser()));
        services.TryAddScoped<ManagementCustomerIdentityTargetFilter>();
        services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            if (!manager.FeatureProviders.OfType<InfoFeature>().Any()) manager.FeatureProviders.Add(new InfoFeature());
        });
        return services;
    }
    private sealed class InfoFeature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var type = typeof(ManagementCustomerIdentityInfoController).GetTypeInfo();
            if (!feature.Controllers.Contains(type)) feature.Controllers.Add(type);
        }
    }
    private sealed class TargetPrincipal(IHttpContextAccessor context) : IManagementPrincipalProvider
    {
        public System.Security.Claims.ClaimsPrincipal GetPrincipal() => context.HttpContext?.User ?? new();
    }
}
