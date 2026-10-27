using System.Security.Claims;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Services.Tenants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
namespace IBeam.Identity.Api.DependencyInjection;

public static class TenantAccessEnforcementExtensions
{
    private sealed class Registration : IIdentityTenantAccessEnforcement
    { public int ProtocolVersion => 1; public string Mode => "ibeam-customer-jwt-live-version"; }
    private sealed record SchemeRegistration(string Name);
    public static IServiceCollection AddIBeamIdentityTenantAccessEnforcementApi(this IServiceCollection services, string bearerScheme = JwtBearerDefaults.AuthenticationScheme)
    {
        if (string.IsNullOrWhiteSpace(bearerScheme)) throw new ArgumentException("A customer bearer scheme is required.", nameof(bearerScheme));
        if (services.Any(x => x.ImplementationInstance is SchemeRegistration registered && registered.Name == bearerScheme)) return services;
        services.AddSingleton(new SchemeRegistration(bearerScheme));
        services.AddIBeamIdentityTenantMembershipAdministration();
        services.TryAddSingleton<IIdentityTenantAccessEnforcement, Registration>();
        services.PostConfigure<JwtBearerOptions>(bearerScheme, options => {
            if (options.EventsType is not null) throw new InvalidOperationException("Custom JWT event types must explicitly integrate current tenant access enforcement.");
            options.Events ??= new(); var previous = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context => {
                if (previous is not null) await previous(context);
                if (context.Result?.Failure is not null || context.Result?.None == true || context.Principal?.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated) return;
                var tenantClaims = identity.Claims.Where(x => x.Type is "tid" or "tenant_id" or "http://schemas.microsoft.com/identity/claims/tenantid").Select(x => x.Value).Distinct(StringComparer.Ordinal).ToArray();
                if (tenantClaims.Length == 0 && identity.HasClaim("pt", "1")) return; // Existing pre-tenant selection endpoints only.
                var users = identity.Claims.Where(x => x.Type is "sub" or "uid" || x.Type == ClaimTypes.NameIdentifier).Select(x => x.Value).Distinct(StringComparer.Ordinal).ToArray();
                var versions = identity.FindAll(IdentityTenantAccessClaims.Version).ToArray();
                if (tenantClaims.Length != 1 || users.Length != 1 || versions.Length != 1 || versions[0].Value.Length != 64 ||
                    !Guid.TryParse(tenantClaims[0], out var tenant) || tenant == Guid.Empty || !Guid.TryParse(users[0], out var user) || user == Guid.Empty) {
                    context.Fail("Current customer tenant access is required."); return;
                }
                try { await context.HttpContext.RequestServices.GetRequiredService<IIdentityTenantAccessValidator>().RequireAsync(user, tenant, versions[0].Value, context.HttpContext.RequestAborted); }
                catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
                catch { context.Fail("Current customer tenant access is required."); }
            };
        });
        services.AddSingleton<IHostedService>(p => new Startup(bearerScheme, p.GetRequiredService<IAuthenticationSchemeProvider>(), p.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()));
        return services;
    }
    private sealed class Startup(string name, IAuthenticationSchemeProvider schemes, IOptionsMonitor<JwtBearerOptions> options) : IHostedService
    {
        public async Task StartAsync(CancellationToken ct) {
            var scheme = await schemes.GetSchemeAsync(name);
            if (scheme is null || !typeof(JwtBearerHandler).IsAssignableFrom(scheme.HandlerType)) throw new InvalidOperationException("A registered customer JWT bearer scheme is required.");
            _ = options.Get(name);
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
