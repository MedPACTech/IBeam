namespace IBeam.Identity.Abstractions.Models;

public sealed record OtpVerifyResult(
    bool Success,
    Guid? UserId = null);
