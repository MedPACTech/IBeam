namespace IBeam.Identity.Abstractions.Models;

public sealed record TenantInfo
(
    Guid TenantId,
    string Name,
    bool IsDefault = false
);
