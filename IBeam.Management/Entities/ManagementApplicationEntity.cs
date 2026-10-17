using System.Text.Json.Serialization;
namespace IBeam.Management.Entities;

public sealed record ManagementApplicationEntity
{
    public Guid OrganizationId { get; init; }
    public Guid ApplicationId { get; init; }
    public string ApplicationKey { get; init; } = "";
    public string Name { get; init; } = "";
    public string Environment { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public bool IsEnabled { get; init; } = true;
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public string CreatedBy { get; init; } = "";
    public string UpdatedBy { get; init; } = "";
    public string Version { get; init; } = "";
    [JsonIgnore] public Guid? ActiveCredentialId { get; init; }
}
