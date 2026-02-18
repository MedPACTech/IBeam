namespace IBeam.Identity.Abstractions.Models;

public sealed record IdentityUser
(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    string? DisplayName = null
);

public sealed record TenantInfo
(
    Guid TenantId,
    string Name,
    bool IsDefault = false
);

public sealed record ClaimItem(string Type, string Value, string? ValueType = null);
