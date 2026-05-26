namespace IBeam.Identity.Models;

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
