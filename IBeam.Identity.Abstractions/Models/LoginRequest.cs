namespace IBeam.Identity.Abstractions.Models;

public sealed record LoginRequest(
    string Email,
    string Password,
    Guid? TenantId = null);
