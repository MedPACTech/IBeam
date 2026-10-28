using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Operations;
using IBeam.Management.Services.Audit;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Management.Services;

internal sealed class ManagementRegisteredRequestState
{
    public ManagementOperationContext? Scope { get; set; }
    public string? Operation { get; set; }
    public string? Issuer { get; set; }
    public Guid CredentialId { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
}
internal sealed class ManagementRegisteredTargetAuthority(ManagementOperationRegistry registry,
    IManagementPrincipalProvider principals, IManagementApiCredentialRepository credentials,
    IOptions<ManagementDiagnosticsOptions> deployment, IOptions<ManagementOperationTargetOptions> options,
    IManagementActorValidationKeyProvider keys, IManagementDelegationReplayStore replay,
    ManagementRegisteredRequestState state, ManagementDelegatedActorState actors, IManagementAuditContext audit,
    IServiceProvider provider, TimeProvider clock) : IManagementOperationAccessService
{
    public const string TokenType = "ibeam-management-operation+jwt";
    public int ProtocolVersion => 1;
    public async Task RequireAsync(string operation, ManagementOperationContext scope, CancellationToken ct = default)
    {
        if (state.Scope is null || scope != state.Scope || operation != state.Operation || state.ExpiresUtc <= clock.GetUtcNow() ||
            scope.RealmId != options.Value.RealmId || !options.Value.DedicatedApplicationRealm ||
            !options.Value.TrustedPortals.Any(x => x.OrganizationId == scope.OrganizationId && x.ApplicationId == scope.ApplicationId && x.Issuer == state.Issuer))
            throw new ManagementAccessDeniedException();
        var credential = await RequireMachine(operation, ct);
        if (credential != state.CredentialId) throw new ManagementAccessDeniedException();
    }
    public async Task<(string Operation, ManagementOperationContext Scope)> VerifyAsync(ManagementOperationInvocation request, CancellationToken ct)
    {
        state.Scope = null; actors.Set(null);
        try
        {
            if (request.Assertion is not { Length: >= 100 and <= 8192 }) throw new ManagementAccessDeniedException();
            Shape(request.Assertion);
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 8192 };
            var candidate = handler.ReadJwtToken(request.Assertion);
            var org = GuidClaim(candidate.Claims, "org"); var app = GuidClaim(candidate.Claims, "app");
            var binding = options.Value.TrustedPortals.SingleOrDefault(x => x.Issuer == candidate.Issuer && x.OrganizationId == org && x.ApplicationId == app)
                ?? throw new ManagementAccessDeniedException();
            var trusted = keys.GetTrustedKeys(binding.Issuer);
            if (trusted is not { Count: > 0 and <= 10 } || trusted.Any(x => !ManagementDelegationBinding.ValidKey(x)) ||
                trusted.Select(x => x.KeyId).Distinct(StringComparer.Ordinal).Count() != trusted.Count || string.IsNullOrWhiteSpace(candidate.Header.Kid))
                throw new ManagementAccessDeniedException();
            var principal = handler.ValidateToken(request.Assertion, new TokenValidationParameters {
                RequireSignedTokens = true, ValidateIssuerSigningKey = true, IssuerSigningKeys = trusted.Where(x => x.KeyId == candidate.Header.Kid),
                ValidateIssuer = true, ValidIssuer = binding.Issuer, ValidateAudience = true, ValidAudience = binding.Audience,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidTypes = [TokenType], RequireExpirationTime = true, ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero, LifetimeValidator = (nbf, exp, _, _) => nbf.HasValue && exp.HasValue &&
                    nbf.Value <= clock.GetUtcNow().UtcDateTime && exp.Value > clock.GetUtcNow().UtcDateTime }, out _);
            var claims = principal.Claims.ToArray(); var operation = Single(claims, "operation");
            var descriptor = registry.Find(operation) ?? throw new ManagementAccessDeniedException();
            if (!request.ReceiptLookup && operation != request.Operation || request.ReceiptLookup && descriptor.ReceiptProtocolVersion != 1)
                throw new ManagementAccessDeniedException();
            if (request.Method == "GET" && (request.IdempotencyKey is not null || request.IfMatch is not null || request.Body.Length != 0) ||
                request.Method != "GET" && (!ManagementDelegationBinding.ValidIdempotencyKey(request.IdempotencyKey) ||
                    request.IfMatch is not { Length: > 0 and <= 256 } || request.IfMatch == "*" || request.IfMatch.Any(char.IsControl)))
                throw new ManagementAccessDeniedException();
            var wire = new ManagementDelegationRequest { Method = request.Method, PathAndQuery = request.Path, Body = request.Body,
                IdempotencyKey = request.IdempotencyKey, IfMatch = request.IfMatch };
            var digest = request.ReceiptLookup ? ManagementDelegationBinding.HashRegisteredReceipt(wire) : ManagementDelegationBinding.Hash(wire, operation, registry);
            var issued = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Single(claims, "iat"), System.Globalization.CultureInfo.InvariantCulture));
            var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Single(claims, "exp"), System.Globalization.CultureInfo.InvariantCulture));
            var notBefore = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Single(claims, "nbf"), System.Globalization.CultureInfo.InvariantCulture));
            var credential = await RequireMachine(operation, ct);
            var scope = new ManagementOperationContext(org, app, GuidClaim(claims, "realm"), GuidClaim(claims, "tenant"),
                GuidClaim(claims, "sub"), GuidClaim(claims, "operation_id", allowEmpty: request.Method == "GET" && !request.ReceiptLookup))
                { ResourceId = GuidClaim(claims, "resource"), ResourceVersion = request.IfMatch };
            if (issued != notBefore || issued > clock.GetUtcNow() || expires <= issued || expires - issued > TimeSpan.FromSeconds(60) ||
                scope.RealmId != options.Value.RealmId || !options.Value.DedicatedApplicationRealm ||
                Single(claims, "application_key") != deployment.Value.ApplicationKey || Single(claims, "environment") != deployment.Value.Environment ||
                GuidClaim(claims, "credential") != credential || Single(claims, "binding") != digest ||
                Single(claims, "iss") != binding.Issuer || Single(claims, "aud") != binding.Audience) throw new ManagementAccessDeniedException();
            if (request.ReceiptLookup)
            {
                var expected = operation == ManagementTenantDataOperations.Export ? TenantData.ManagementTenantExportRoutes.StatusPath(scope.TenantId, scope.OperationId) :
                    "/api/management-operations/receipts/" + scope.OperationId.ToString("D");
                if (request.Path != expected) throw new ManagementAccessDeniedException();
            }
            else if (ManagementRegisteredRoute.Path(descriptor, scope.TenantId, scope.ResourceId) != request.Path) throw new ManagementAccessDeniedException();
            var correlation = Single(claims, "correlation");
            if (correlation.Length != 32 || !correlation.All(x => x is >= 'a' and <= 'f' or >= '0' and <= '9')) throw new ManagementAccessDeniedException();
            if (request.Method != "GET" && !await replay.TryClaimAsync(scope.RealmId, binding.Issuer, GuidClaim(claims, "jti"), expires, ct))
                throw new ManagementAccessDeniedException();
            state.Scope = scope; state.Operation = operation; state.Issuer = binding.Issuer; state.CredentialId = credential; state.ExpiresUtc = expires;
            actors.Set(new(org, scope.ActorUserId, app, scope.RealmId, credential, deployment.Value.ApplicationKey, deployment.Value.Environment,
                operation, GuidClaim(claims, "jti"), expires, correlation)); audit.AcceptMachineCorrelation(correlation);
            return (operation, scope);
        }
        catch (OperationCanceledException) { throw; }
        catch { state.Scope = null; actors.Set(null); throw new ManagementAccessDeniedException(); }
    }
    private async Task<Guid> RequireMachine(string operation, CancellationToken ct)
    {
        var identity = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Credential, operation);
        var configured = deployment.Value; var id = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.CredentialId);
        if (ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.ApplicationKey) != configured.ApplicationKey ||
            ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.Environment) != configured.Environment) throw new ManagementAccessDeniedException();
        var row = await credentials.GetAsync(configured.ApplicationKey, configured.Environment, id, ct);
        if (row is null || row.CredentialId != id || row.ApplicationKey != configured.ApplicationKey || row.Environment != configured.Environment ||
            row.Status != "Active" || row.RevokedUtc.HasValue || row.ExpiresUtc <= clock.GetUtcNow() || !registry.ValidCapabilities(row.Capabilities) ||
            row.Version != ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.CredentialVersion)) throw new ManagementAccessDeniedException();
        foreach (var required in registry.RequiredFor(operation))
            if (!row.Capabilities.Contains(required, StringComparer.Ordinal) || !options.Value.EnabledOperations.Contains(required, StringComparer.Ordinal) || !Installed(required))
                throw new ManagementAccessDeniedException();
        return id;
    }
    private bool Installed(string operation)
    {
        if (registry.Find(operation) is { } descriptor)
        {
            var instance = provider.GetService(descriptor.HandlerServiceType);
            return instance is not null && descriptor.HandlerServiceType.IsInstanceOfType(instance) && (descriptor.ReceiptProtocolVersion == 0 ||
                typeof(IManagementOperationReceiptHandler<>).MakeGenericType(descriptor.Result.ContractType).IsInstanceOfType(instance));
        }
        return provider.GetServices<IManagementCustomerIdentityCapability>().Any(x => x.Operation == operation && x.RequiredServiceType.IsInterface && provider.GetService(x.RequiredServiceType) is not null);
    }
    private static string Single(IEnumerable<Claim> claims, string name)
    { var values = claims.Where(x => x.Type == name).Select(x => x.Value).ToArray(); return values.Length == 1 && !string.IsNullOrWhiteSpace(values[0]) ? values[0] : throw new ManagementAccessDeniedException(); }
    private static Guid GuidClaim(IEnumerable<Claim> claims, string name, bool allowEmpty = false)
        => Guid.TryParseExact(Single(claims, name), "D", out var id) && (allowEmpty || id != Guid.Empty) ? id : throw new ManagementAccessDeniedException();
    private static void Shape(string token)
    {
        var parts = token.Split('.'); if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace)) throw new ManagementAccessDeniedException();
        using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]), new JsonDocumentOptions { MaxDepth = 4 });
        var names = header.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
        if (names.Length != 3 || names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(x => x is not ("typ" or "kid" or "alg"))) throw new ManagementAccessDeniedException();
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]), new JsonDocumentOptions { MaxDepth = 4 });
        var properties = payload.RootElement.EnumerateObject().ToArray();
        if (properties.Length > 24 || properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length ||
            payload.RootElement.GetProperty("aud").ValueKind != JsonValueKind.String) throw new ManagementAccessDeniedException();
    }
}
internal static class ManagementRegisteredRoute
{
    public static string Path(ManagementOperationDescriptor descriptor, Guid tenant, Guid resource)
    {
        if (tenant == Guid.Empty || resource == Guid.Empty) throw new ManagementOperationInvalidException();
        // Protocol 1 carries one tenant and one selected resource. Never silently bind
        // multiple independently named resources to the same signed identifier.
        if (System.Text.RegularExpressions.Regex.Matches(descriptor.RouteTemplate, "\\{([a-z][a-zA-Z0-9]{0,31}):guid\\}")
            .Cast<System.Text.RegularExpressions.Match>().Count(x => x.Groups[1].Value != "tenantId") > 1)
            throw new ManagementOperationInvalidException();
        return System.Text.RegularExpressions.Regex.Replace(descriptor.RouteTemplate, "\\{([a-z][a-zA-Z0-9]{0,31}):guid\\}",
            match => (match.Groups[1].Value == "tenantId" ? tenant : resource).ToString("D"));
    }
}
