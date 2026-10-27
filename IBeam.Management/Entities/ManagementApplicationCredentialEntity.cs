using System.Text.Json.Serialization;
namespace IBeam.Management.Entities;

/// <summary>Verified connection version. Secret material is owned by the external store, never this row.</summary>
public sealed record ManagementApplicationCredentialEntity
{
    public Guid OrganizationId { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid CredentialId { get; init; }
    [JsonIgnore] public string SecretReference { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public DateTimeOffset VerifiedUtc { get; init; }
    public string CreatedBy { get; init; } = "";
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CustomerIdentity.ManagementCustomerIdentityDiscovery? CustomerIdentity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Operations.ManagementRegisteredOperationDiscovery? Operations { get; init; }
    public override string ToString() => "Management connection credential (reference redacted)";
}
