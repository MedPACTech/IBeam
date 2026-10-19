using System.Security.Cryptography;
using System.Text;
using IBeam.Management.CustomerIdentity;
using Microsoft.IdentityModel.Tokens;
namespace IBeam.Management.Services.CustomerIdentity.Delegation;

public interface IManagementActorSigningKeyProvider { SigningCredentials GetSigningCredentials(); }
public interface IManagementActorValidationKeyProvider { IReadOnlyList<SecurityKey> GetTrustedKeys(string configuredIssuer); }

public sealed class ManagementActorIssuerOptions
{
    public const string SectionName = "IBeam:Management:Delegation:Issuer";
    public string Issuer { get; set; } = "";
    public int LifetimeSeconds { get; set; } = 30;
    public List<ManagementActorTargetBinding> Targets { get; set; } = [];
}
public sealed class ManagementActorTargetBinding
{
    public Guid ApplicationId { get; set; }
    public string Audience { get; set; } = "";
}
public static class ManagementDelegationBinding
{
    public static string Hash(ManagementDelegationRequest request)
    {
        if (request is null || request.Method is not ("GET" or "POST" or "PATCH" or "PUT" or "DELETE") ||
            request.PathAndQuery is not { Length: > 0 and <= 16384 } ||
            !request.PathAndQuery.StartsWith("/api/management-identity/", StringComparison.Ordinal) ||
            request.PathAndQuery.Any(char.IsControl) || request.PathAndQuery.Contains('#') || request.Body.Length > 32768 ||
            request.Method == "GET" && (!request.Body.IsEmpty || request.IdempotencyKey is not null) ||
            request.Method != "GET" && !ValidIdempotencyKey(request.IdempotencyKey)) throw new ManagementAccessDeniedException();
        var bound = request.Method + "\n" + request.PathAndQuery + "\n" + Convert.ToHexString(SHA256.HashData(request.Body.Span));
        if (request.Method != "GET") bound += "\n" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.IdempotencyKey!)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bound))).ToLowerInvariant();
    }
    public static bool ValidIdempotencyKey(string? value) => value is { Length: >= 8 and <= 128 } && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.');
    public static bool IsReceiptRead(ManagementDelegationRequest request) => request.Method == "GET" &&
        request.PathAndQuery.StartsWith("/api/management-identity/operations/", StringComparison.Ordinal) &&
        Guid.TryParseExact(request.PathAndQuery["/api/management-identity/operations/".Length..], "D", out var id) && id != Guid.Empty;
    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 200 && !name.Any(char.IsControl);
    public static bool ValidKey(SecurityKey key) => key is RsaSecurityKey && key.KeySize >= 2048 &&
        key.KeyId is { Length: > 0 and <= 100 } && key.KeyId.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_');
    public static bool Mutating(string operation) => ManagementCustomerIdentityPermissionPolicy.IsAssignable(operation) &&
        operation is not (ManagementCustomerIdentityOperations.TenantsRead or ManagementCustomerIdentityOperations.UsersRead or
            ManagementCustomerIdentityOperations.InvitesRead or ManagementCustomerIdentityOperations.OnboardingRead);
}
