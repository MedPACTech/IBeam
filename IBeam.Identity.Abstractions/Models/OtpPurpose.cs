namespace IBeam.Identity.Abstractions.Models;

public enum OtpPurpose
{
    EmailVerification,
    LoginMfa,
    PasswordReset,
    UserRegistration,
    PhoneVerification,
    ChangeEmail,
    ChangePhone
}
