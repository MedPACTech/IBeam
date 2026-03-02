namespace IBeam.Identity.Abstractions.Models;

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record RevokeSessionRequest(string SessionId);
