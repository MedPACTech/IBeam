namespace IBeam.Identity.Models;

public sealed record LoginRequest(
    string Email,
    string Password,
    Guid? TenantId = null);
