namespace IBeam.Identity.Abstractions.Models;

public enum SenderPurpose
{
    EmailVerification,
    LoginMfa,
    PasswordReset,
    UserRegistration,
    PhoneVerification,
    ChangeEmail,
    ChangePhone
}
