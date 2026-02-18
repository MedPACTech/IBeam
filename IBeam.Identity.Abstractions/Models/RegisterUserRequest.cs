namespace IBeam.Identity.Abstractions.Models;

public sealed record RegisterUserRequest(
    string Email,
    string Password,
    string? DisplayName = null);
