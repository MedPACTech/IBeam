using System.Text.Json.Serialization;
using IBeam.Management.Entities;

namespace IBeam.Management;

public interface IManagementApiCredentialRepository
{
    Task<ManagementApiCredentialEntity?> GetAsync(string applicationKey, string environment, Guid credentialId, CancellationToken ct = default);
    Task<ManagementApiCredentialEntity> CreateAsync(ManagementApiCredentialEntity credential, CancellationToken ct = default);
    /// <summary>Compare-and-swap; a stale version must never overwrite a concurrent revocation.</summary>
    Task<ManagementApiCredentialEntity> ReplaceAsync(ManagementApiCredentialEntity credential, string expectedVersion, CancellationToken ct = default);
}

public sealed class ManagementCredentialConflictException : Exception
{
    public ManagementCredentialConflictException() : base("Management credential changed concurrently.") { }
}

public sealed record ManagementCredentialMetadata(
    Guid CredentialId, string ApplicationKey, string Environment,
    IReadOnlyList<string> Capabilities, string Status, DateTimeOffset ExpiresUtc);

public sealed class ManagementIssuedCredential
{
    public required ManagementCredentialMetadata Credential { get; init; }
    /// <summary>Read once in trusted tooling and send directly to the secret store. Never log/serialize the result.</summary>
    [JsonIgnore] public string ApiKey { get; init; } = "";
    public override string ToString() => $"Issued management credential {Credential.CredentialId:D} (API key redacted)";
}

/// <summary>Host-owned trusted operator authorization. Never derive this from a portal/customer role or request header.</summary>
public interface IManagementCredentialOperatorProvider
{
    Task<Guid> RequireAuthorizedOperatorAsync(CancellationToken ct = default);
}

public interface IManagementCredentialProvisioningService
{
    Task<ManagementIssuedCredential> IssueAsync(IReadOnlyList<string> capabilities, DateTimeOffset expiresUtc, CancellationToken ct = default);
    Task<ManagementIssuedCredential> RotateAsync(Guid credentialId, CancellationToken ct = default);
    Task RevokeAsync(Guid credentialId, CancellationToken ct = default);
}

public interface IManagementCredentialValidator
{
    Task<ManagementCredentialMetadata?> ValidateAsync(string apiKey, CancellationToken ct = default);
}
