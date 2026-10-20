using System.Text.Json.Serialization;
namespace IBeam.Management.Entities;

/// <summary>Dedicated target metadata only. No customer request, names, emails, secrets or response bodies.</summary>
public sealed record ManagementIdentityOperationEntity
{
    public Guid IdentityRealmId { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid ApplicationId { get; init; }
    public string ApplicationKey { get; init; } = "";
    public string Environment { get; init; } = "";
    public Guid OperationId { get; init; }
    public string Operation { get; init; } = "";
    public Guid ResourceScope { get; init; }
    public Guid ResourceId { get; init; }
    [JsonIgnore] public string Fingerprint { get; init; } = "";
    public string Status { get; init; } = "pending";
    public int Step { get; init; }
    public Guid ActorUserId { get; init; }
    public Guid CredentialId { get; init; }
    public string CorrelationId { get; init; } = "";
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }
    [JsonIgnore] public Guid? LeaseId { get; init; }
    public DateTimeOffset? LeaseUntilUtc { get; init; }
    public int Attempts { get; init; }
    public Guid? AttemptActorUserId { get; init; }
    public Guid? AttemptCredentialId { get; init; }
    public string? AttemptCorrelationId { get; init; }
    public Guid IntentAuditId { get; init; }
    public bool IntentPending { get; init; } = true;
    public Guid? ResultAuditId { get; init; }
    public DateTimeOffset? ResultUtc { get; init; }
    public string? ResultStatus { get; init; }
    public bool ResultPending { get; init; }
    public string Version { get; init; } = "";
    public override string ToString() => "Management operation receipt (internal state redacted)";
}
