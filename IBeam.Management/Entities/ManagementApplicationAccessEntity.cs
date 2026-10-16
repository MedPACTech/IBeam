namespace IBeam.Management.Entities;

public sealed record ManagementApplicationAccessEntity
{
    public Guid OrganizationId { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid UserId { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public Guid UpdatedBy { get; init; }
    public DateTimeOffset? RevokedUtc { get; init; }
    public Guid? RevokedBy { get; init; }
    public string Version { get; init; } = "";
}
