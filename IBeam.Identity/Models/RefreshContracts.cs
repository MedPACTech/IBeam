namespace IBeam.Identity.Models;

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record RevokeSessionRequest(string SessionId);
