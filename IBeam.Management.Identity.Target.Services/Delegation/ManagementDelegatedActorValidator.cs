using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
namespace IBeam.Management.Identity.Target.Services.Delegation;

internal sealed class ManagementDelegatedActorContext : IManagementDelegatedActorContext
{
    public ManagementDelegatedActor? Current { get; private set; }
    internal void Set(ManagementDelegatedActor? actor) => Current = actor;
}
internal sealed class ManagementDelegatedActorValidator(ManagementCustomerIdentityCredentialGuard credentials,
    IOptions<ManagementActorTrustOptions> trust, IOptions<ManagementCustomerIdentityTargetOptions> realm,
    IOptions<ManagementDiagnosticsOptions> deployment, IManagementActorValidationKeyProvider keys,
    IManagementDelegationReplayStore replay, ManagementDelegatedActorContext context, IManagementAuditContext audit,
    TimeProvider clock, IServiceOperationExecutor operations) : IManagementDelegatedActorValidator
{
    public Task<ManagementDelegatedActor> ValidateAsync(string assertion, string operation, ManagementDelegationRequest request,
        ClaimsPrincipal authenticatedMachine, CancellationToken ct = default)
        => Validate(assertion, operation, request, authenticatedMachine, ct);
    public Task<ManagementDelegatedActor> ValidateReceiptAsync(string assertion, ManagementDelegationRequest request,
        ClaimsPrincipal authenticatedMachine, CancellationToken ct = default)
        => Validate(assertion, null, request, authenticatedMachine, ct);
    private Task<ManagementDelegatedActor> Validate(string assertion, string? operation, ManagementDelegationRequest request,
        ClaimsPrincipal authenticatedMachine, CancellationToken ct)
        => operations.ExecuteAsync(this, async token =>
        {
            context.Set(null);
            try
            {
                var receipt = operation is null;
                if (assertion is not { Length: > 0 and <= 8192 } || receipt && !ManagementDelegationBinding.IsReceiptRead(request) ||
                    !receipt && ManagementDelegationBinding.Mutating(operation!) && request.Method == "GET")
                    throw new ManagementAccessDeniedException();
                ValidateShape(assertion);
                var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 8192 };
                var candidate = handler.ReadJwtToken(assertion);
                var candidateOrg = GuidClaim(candidate.Claims, "org"); var candidateApp = GuidClaim(candidate.Claims, "app");
                var binding = trust.Value.TrustedPortals.SingleOrDefault(x => x.Issuer == candidate.Issuer &&
                    x.OrganizationId == candidateOrg && x.ApplicationId == candidateApp) ?? throw new ManagementAccessDeniedException();
                var trusted = keys.GetTrustedKeys(binding.Issuer);
                if (trusted is not { Count: > 0 and <= 10 } || trusted.Any(x => !ManagementDelegationBinding.ValidKey(x)) ||
                    trusted.Select(x => x.KeyId).Distinct(StringComparer.Ordinal).Count() != trusted.Count ||
                    string.IsNullOrWhiteSpace(candidate.Header.Kid)) throw new ManagementAccessDeniedException();
                var principal = handler.ValidateToken(assertion, new TokenValidationParameters {
                    RequireSignedTokens = true, ValidateIssuerSigningKey = true, IssuerSigningKeys = trusted.Where(x => x.KeyId == candidate.Header.Kid),
                    ValidateIssuer = true, ValidIssuer = binding.Issuer, ValidateAudience = true, ValidAudience = binding.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidTypes = ["ibeam-management-actor+jwt"],
                    RequireExpirationTime = true, ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                    LifetimeValidator = (nbf, exp, _, _) => nbf.HasValue && exp.HasValue && nbf.Value <= clock.GetUtcNow().UtcDateTime &&
                        exp.Value > clock.GetUtcNow().UtcDateTime
                }, out _);
                var claims = principal.Claims.ToArray();
                if (receipt)
                {
                    operation = Single(claims, "operation");
                    if (!ManagementDelegationBinding.Mutating(operation) && operation != ManagementCustomerIdentityOperations.OnboardingRead)
                        throw new ManagementAccessDeniedException();
                }
                var credentialId = await credentials.RequireAsync(authenticatedMachine, operation!, token);
                var issued = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Single(claims, "iat"), System.Globalization.CultureInfo.InvariantCulture));
                var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Single(claims, "exp"), System.Globalization.CultureInfo.InvariantCulture));
                var notBefore = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Single(claims, "nbf"), System.Globalization.CultureInfo.InvariantCulture));
                var now = clock.GetUtcNow();
                if (issued > now || issued != notBefore || expires <= now || expires <= issued || expires - issued > TimeSpan.FromSeconds(60) ||
                    Single(claims, "aud") != binding.Audience || Single(claims, "iss") != binding.Issuer ||
                    Single(claims, "operation") != operation || Single(claims, "application_key") != deployment.Value.ApplicationKey ||
                    Single(claims, "environment") != deployment.Value.Environment || GuidClaim(claims, "realm") != realm.Value.IdentityRealmId ||
                    GuidClaim(claims, "credential") != credentialId || GuidClaim(claims, "org") != binding.OrganizationId ||
                    GuidClaim(claims, "app") != binding.ApplicationId ||
                    Single(claims, "binding") != ManagementDelegationBinding.Hash(request))
                    throw new ManagementAccessDeniedException();
                var correlation = Single(claims, "correlation");
                if (correlation.Length != 32 || !correlation.All(x => x is >= 'a' and <= 'f' or >= '0' and <= '9')) throw new ManagementAccessDeniedException();
                var actor = new ManagementDelegatedActor(binding.OrganizationId, GuidClaim(claims, "sub"), binding.ApplicationId,
                    realm.Value.IdentityRealmId, credentialId, deployment.Value.ApplicationKey, deployment.Value.Environment,
                    operation!, GuidClaim(claims, "jti"), expires, correlation);
                if (request.Method != "GET" &&
                    !await replay.TryClaimAsync(actor.IdentityRealmId, binding.Issuer, actor.AssertionId, expires, token))
                    throw new ManagementAccessDeniedException();
                context.Set(actor); audit.AcceptMachineCorrelation(correlation);
                return actor;
            }
            catch (OperationCanceledException) { throw; }
            catch { throw new ManagementAccessDeniedException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.identity.delegation.validate",
            AuditAction = "management.identity.delegation.validate", EntityName = "ManagementDelegatedActor",
            PermissionEnabled = false, AuditEnabled = true }, ct);

    private static string Single(IEnumerable<Claim> claims, string type)
    {
        var values = claims.Where(x => x.Type == type).Select(x => x.Value).ToArray();
        return values.Length == 1 && !string.IsNullOrWhiteSpace(values[0]) ? values[0] : throw new ManagementAccessDeniedException();
    }
    private static Guid GuidClaim(IEnumerable<Claim> claims, string type) => Guid.TryParseExact(Single(claims, type), "D", out var id) && id != Guid.Empty
        ? id : throw new ManagementAccessDeniedException();
    private static void ValidateShape(string assertion)
    {
        var segments = assertion.Split('.');
        if (segments.Length != 3 || segments.Any(string.IsNullOrWhiteSpace)) throw new ManagementAccessDeniedException();
        using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segments[0]), new JsonDocumentOptions { MaxDepth = 4 });
        var properties = header.RootElement.EnumerateObject().ToArray();
        if (properties.Length != 3 || properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 3 ||
            properties.Any(x => x.Name is not ("typ" or "kid" or "alg"))) throw new ManagementAccessDeniedException();
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segments[1]), new JsonDocumentOptions { MaxDepth = 4 });
        var fields = payload.RootElement.EnumerateObject().ToArray();
        if (fields.Length > 20 || fields.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length ||
            payload.RootElement.GetProperty("aud").ValueKind != JsonValueKind.String) throw new ManagementAccessDeniedException();
    }
}
internal sealed class ManagementCustomerIdentityTargetAccessService(ManagementCustomerIdentityCredentialGuard credentials,
    IManagementPrincipalProvider principals, IManagementDelegatedActorContext context, TimeProvider clock, IServiceOperationExecutor operations,
    IOptions<ManagementCustomerIdentityTargetOptions> realm, IOptions<ManagementDiagnosticsOptions> deployment)
    : IManagementCustomerIdentityTargetAccessService
{
    public Task<ManagementDelegatedActor> RequireAsync(string operation, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            var actor = context.Current ?? throw new ManagementAccessDeniedException();
            if (actor.Operation != operation || actor.ExpiresUtc <= clock.GetUtcNow() || actor.IdentityRealmId != realm.Value.IdentityRealmId ||
                actor.ApplicationKey != deployment.Value.ApplicationKey || actor.Environment != deployment.Value.Environment)
                throw new ManagementAccessDeniedException();
            var credential = await credentials.RequireAsync(principals.GetPrincipal(), operation, token);
            if (actor.CredentialId != credential) throw new ManagementAccessDeniedException();
            return actor;
        }, new ServiceOperationExecutionOptions { OperationName = "management.identity.delegation.require",
            AuditAction = "management.identity.delegation.require", EntityName = "ManagementDelegatedActor",
            PermissionEnabled = false, AuditEnabled = true }, ct);
}
