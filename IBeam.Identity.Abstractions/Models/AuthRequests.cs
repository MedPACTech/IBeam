namespace IBeam.Identity.Abstractions.Models;

public sealed record RegisterRequest(
    string Email,
    string? PhoneNumber,
    string? Password);

public sealed record PasswordLoginRequest(
    string Email,
    string Password);

public sealed record RequestOtpRequest(
    string Identifier,      // email or phone (we'll support both)
    SenderChannel Channel);

public sealed record VerifyOtpRequest(
    string Identifier,      // email or phone
    SenderChannel Channel,
    string Code);
