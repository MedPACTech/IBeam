namespace IBeam.Identity.Models;

public sealed record LinkedOAuthProvider(
    string Provider,
    string? Email);
