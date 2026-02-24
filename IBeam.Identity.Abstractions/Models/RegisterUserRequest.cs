namespace IBeam.Identity.Abstractions.Models;

public sealed record RegisterUserRequest(
    string? Email,
    string? PhoneNumber,
    string Password,
    string? DisplayName = null);
