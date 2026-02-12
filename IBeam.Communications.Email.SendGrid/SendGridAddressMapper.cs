using IBeam.Communications.Abstractions;
using SendGrid.Helpers.Mail;
using EmailAddress = SendGrid.Helpers.Mail.EmailAddress;


// Alias our type to avoid ambiguity if needed later
using IBeamEmailAddress = IBeam.Communications.Abstractions.EmailAddress;

namespace IBeam.Communications.Email.SendGrid;

internal static class SendGridAddressMapper
{
    public static EmailAddress ToSendGrid(IBeamEmailAddress address)
    {
        if (address is null) throw new ArgumentNullException(nameof(address));
        return new EmailAddress(address.Address, address.DisplayName);
    }

    public static IBeamEmailAddress ToIBeam(EmailAddress address)
    {
        if (address is null) throw new ArgumentNullException(nameof(address));
        return new IBeamEmailAddress(address.Email, address.Name);
    }
}
