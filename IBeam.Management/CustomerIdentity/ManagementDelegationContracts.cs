using System.Security.Claims;
using System.Text.Json.Serialization;
namespace IBeam.Management.CustomerIdentity;

public sealed record ManagementDelegatedActor(Guid OrganizationId, Guid UserId, Guid ApplicationId, Guid IdentityRealmId,
    Guid CredentialId, string ApplicationKey, string Environment, string Operation, Guid AssertionId,
    DateTimeOffset ExpiresUtc, string CorrelationId);
public sealed class ManagementActorAssertion
{
    [JsonIgnore] public string Value { get; init; } = "";
    public required DateTimeOffset ExpiresUtc { get; init; }
    public override string ToString() => "Management actor assertion (redacted)";
}
/// <summary>Exact outgoing method, encoded path/query and serialized UTF-8 body, not browser authorization claims.</summary>
public sealed class ManagementDelegationRequest
{
    public required string Method { get; init; }
    [JsonIgnore] public string PathAndQuery { get; init; } = "";
    [JsonIgnore] public ReadOnlyMemory<byte> Body { get; init; }
    public override string ToString() => "Management delegated request (binding redacted)";
}
public interface IManagementDelegatedActorProvider
{
    Task<ManagementActorAssertion> IssueAsync(Guid applicationId, string operation, ManagementDelegationRequest request, CancellationToken ct = default);
}
public interface IManagementDelegatedActorValidator
{
    Task<ManagementDelegatedActor> ValidateAsync(string assertion, string operation, ManagementDelegationRequest request,
        ClaimsPrincipal authenticatedMachine, CancellationToken ct = default);
}
public interface IManagementDelegatedActorContext { ManagementDelegatedActor? Current { get; } }
public interface IManagementCustomerIdentityTargetAccessService
{
    Task<ManagementDelegatedActor> RequireAsync(string operation, CancellationToken ct = default);
}
public interface IManagementDelegationReplayStore
{
    /// <summary>Atomic insert only; no schema creation. An unexpired assertion ID can be claimed once across target instances.</summary>
    Task<bool> TryClaimAsync(Guid realmId, string issuer, Guid assertionId, DateTimeOffset expiresUtc, CancellationToken ct = default);
}
