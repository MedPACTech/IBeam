namespace IBeam.Identity.Models;

public sealed record TenantMetadata(
    Guid TenantId,
    string? Name = null,
    string? DisplayName = null,
    string? NormalizedName = null,
    bool? IsActive = null,
    IReadOnlyDictionary<string, string>? Attributes = null);
