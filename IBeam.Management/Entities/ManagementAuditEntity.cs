namespace IBeam.Management.Entities;
/// <summary>Append-only support audit metadata. No payload, exception, key, hash, header or entity snapshot fields.</summary>
public sealed record ManagementAuditEntity
{
    public Guid AuditId { get; init; }
    public DateTimeOffset OccurredUtc { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }
    public Guid? OrganizationId { get; init; }
    public Guid? ActorUserId { get; init; }
    public Guid? CredentialId { get; init; }
    public Guid? ApplicationId { get; init; }
    public Guid? SubjectId { get; init; }
    public string SubjectType { get; init; } = "unknown";
    public string? ApplicationKey { get; init; }
    public string? Environment { get; init; }
    public string Operation { get; init; } = "";
    public bool Succeeded { get; init; }
    public string CorrelationId { get; init; } = "";
}
