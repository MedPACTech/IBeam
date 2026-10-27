using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.Audit;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Hosting;
namespace IBeam.Management.Services.CustomerIdentity.Delegation;

/// <summary>Server-only assertion issuer. Authentication/assignment state and active machine key are resolved before signing.</summary>
public sealed class ManagementDelegatedActorProvider(IManagementCustomerIdentityAuthorizationService authorization,
    IManagementPrincipalProvider principals, IManagementApplicationStateProvider applications,
    IManagementApplicationCredentialRepository credentials, IManagementSecretStore secrets, ManagementCredentialSecretCodec codec,
    IManagementActorSigningKeyProvider keys, IOptionsMonitor<ManagementActorIssuerOptions> options,
    IManagementAuditContext audit, TimeProvider clock, IServiceOperationExecutor operations) : IManagementDelegatedActorProvider
{
    public Task<ManagementActorAssertion> IssueAsync(Guid applicationId, string operation, ManagementDelegationRequest request, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            try
            {
            await authorization.RequireAsync(applicationId, operation, token);
            var identity = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Human, ManagementOperations.IdentityRead);
            var organization = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.OrganizationId);
            if (!Guid.TryParse(ManagementPrincipalGuard.RequireHumanUserId(identity), out var user) || user == Guid.Empty) throw new ManagementAccessDeniedException();
            var configured = options.CurrentValue;
            var binding = configured.Targets.SingleOrDefault(x => x.ApplicationId == applicationId) ?? throw new ManagementAccessDeniedException();
            var digest = ManagementDelegationBinding.Hash(request);
            var app = await applications.GetAsync(organization, applicationId, token);
            if (app is null || !app.IsEnabled || app.OrganizationId != organization || app.ApplicationId != applicationId || app.ActiveCredentialId is not Guid credentialId)
                throw new ManagementAccessDeniedException();
            var connection = await credentials.GetAsync(organization, applicationId, credentialId, token);
            if (connection is null || connection.OrganizationId != organization || connection.ApplicationId != applicationId ||
                connection.CredentialId != credentialId || connection.BaseUrl != app.BaseUrl ||
                connection.CustomerIdentity is not { Status: ManagementCustomerIdentityStatuses.Available, Info: { } info })
                throw new ManagementAccessDeniedException();
            ManagementCustomerIdentityProtocol.ValidateProfile(new(null, connection.CustomerIdentity), app.ApplicationKey, app.Environment);
            var secret = await secrets.ReadAsync(new(organization, applicationId), connection.SecretReference, token);
            if (!codec.TryReadId(secret.Value, out var targetCredential)) throw new ManagementAccessDeniedException();
            await authorization.RequireAsync(applicationId, operation, token);
            var current = await applications.GetAsync(organization, applicationId, token);
            if (current is null || !current.IsEnabled || current.Version != app.Version || current.ActiveCredentialId != credentialId)
                throw new ManagementAccessDeniedException();
            var signing = keys.GetSigningCredentials();
            if (signing.Algorithm != SecurityAlgorithms.RsaSha256 || !ManagementDelegationBinding.ValidKey(signing.Key)) throw new ManagementAccessDeniedException();
            var now = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
            var expires = now.AddSeconds(configured.LifetimeSeconds);
            var claims = new List<Claim> {
                new("sub", user.ToString("D")), new("org", organization.ToString("D")), new("app", applicationId.ToString("D")),
                new("realm", info.IdentityRealmId.ToString("D")), new("credential", targetCredential.ToString("D")),
                new("application_key", app.ApplicationKey), new("environment", app.Environment), new("operation", operation),
                new("binding", digest), new("jti", Guid.NewGuid().ToString("D")), new("correlation", audit.CorrelationId),
                new("iat", now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64) };
            var jwt = new JwtSecurityToken(configured.Issuer, binding.Audience, claims, now.UtcDateTime, expires.UtcDateTime, signing);
            jwt.Header["typ"] = "ibeam-management-actor+jwt";
            return new ManagementActorAssertion { Value = new JwtSecurityTokenHandler().WriteToken(jwt), ExpiresUtc = expires };
            }
            catch (OperationCanceledException) { throw; }
            catch (ManagementAccessDeniedException) { throw; }
            catch { throw new ManagementConnectionUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.portal.customer-identity.delegate",
            AuditAction = "management.portal.customer-identity.delegate", EntityId = applicationId, EntityName = "ManagementDelegatedActor",
            PermissionEnabled = false, AuditEnabled = true }, ct);
}
public static class ManagementActorIssuerServiceExtensions
{
    private sealed class Registration;
    /// <summary>The host supplies a dedicated signing-key provider and durable metadata audit repository.</summary>
    public static IServiceCollection AddIBeamManagementActorDelegation(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>();
        services.AddIBeamManagementCustomerIdentity(configuration);
        services.AddIBeamManagementAudit(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ManagementCredentialSecretCodec>();
        services.AddOptions<ManagementActorIssuerOptions>().Bind(configuration.GetSection(ManagementActorIssuerOptions.SectionName))
            .Validate(x => ManagementDelegationBinding.ValidName(x.Issuer) && x.LifetimeSeconds is >= 1 and <= 60 &&
                x.Targets is { Count: > 0 and <= 100 } && x.Targets.All(t => t is not null && t.ApplicationId != Guid.Empty && ManagementDelegationBinding.ValidName(t.Audience)) &&
                x.Targets.Select(t => t.ApplicationId).Distinct().Count() == x.Targets.Count,
                "A named issuer, explicit target audiences and a 1-60 second assertion lifetime are required.").ValidateOnStart();
        services.TryAddScoped<IManagementDelegatedActorProvider, ManagementDelegatedActorProvider>();
        services.AddHostedService<DependencyValidator>();
        return services;
    }
    private sealed class DependencyValidator(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IManagementDelegatedActorProvider>();
            _ = scope.ServiceProvider.GetRequiredService<IManagementAuditRepository>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
