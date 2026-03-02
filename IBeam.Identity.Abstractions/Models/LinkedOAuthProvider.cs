namespace IBeam.Identity.Abstractions.Models;

public sealed record LinkedOAuthProvider(
    string Provider,
    string? Email);
