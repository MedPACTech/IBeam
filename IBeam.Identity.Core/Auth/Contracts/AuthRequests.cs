namespace IBeam.Identity.Core.Auth.Contracts;

public sealed record RegisterRequest(
    string Email,
    string? PhoneNumber,
    string? Password);

public sealed record PasswordLoginRequest(
    string Email,
    string Password);

public sealed record RequestOtpRequest(
    string Identifier,      // email or phone (we'll support both)
    OtpChannel Channel);

public sealed record VerifyOtpRequest(
    string Identifier,      // email or phone
    OtpChannel Channel,
    string Code);
