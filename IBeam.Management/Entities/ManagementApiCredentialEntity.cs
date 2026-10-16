using System.Text.Json.Serialization;

namespace IBeam.Management.Entities;

/// <summary>Stored deployment credential; never use the entity as a public response or audit snapshot.</summary>
public sealed record ManagementApiCredentialEntity
{
    public Guid CredentialId { get; init; }
    public string ApplicationKey { get; init; } = "";
    public string Environment { get; init; } = "";
    [JsonIgnore] public string SecretHash { get; init; } = "";
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public string Status { get; init; } = "Active";
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }
    public DateTimeOffset? RotatedUtc { get; init; }
    public DateTimeOffset? RevokedUtc { get; init; }
    public Guid CreatedByOperatorId { get; init; }
    public Guid? RotatedByOperatorId { get; init; }
    public Guid? RevokedByOperatorId { get; init; }
    [JsonIgnore] public string Version { get; init; } = "";
    public override string ToString() => $"Management credential {CredentialId:D} (secret hash redacted)";
}
