using IBeam.Communications.Abstractions;

namespace IBeam.Communications.Abstractions.Validation;

public static class EmailMessageValidator
{
    public static void Validate(EmailMessage message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        if (message.To is null || message.To.Count == 0 || message.To.All(string.IsNullOrWhiteSpace))
            throw new EmailValidationException("EmailMessage.To must contain at least one recipient.");

        if (string.IsNullOrWhiteSpace(message.Subject))
            throw new EmailValidationException("EmailMessage.Subject is required.");

        var hasBody = !string.IsNullOrWhiteSpace(message.HtmlBody) || !string.IsNullOrWhiteSpace(message.TextBody);
        if (!hasBody)
            throw new EmailValidationException("EmailMessage must include HtmlBody or TextBody.");
    }
}
