namespace IBeam.Communications.Email.Abstractions;

public static class EmailDefaults
{
    public static void ValidateMessageForSend(string providerName, EmailMessage message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        if (message.To is null || message.To.Count == 0)
            throw new EmailValidationException(providerName, "At least one recipient is required.");

        if (string.IsNullOrWhiteSpace(message.Subject))
            throw new EmailValidationException(providerName, "Subject is required.");

        if (string.IsNullOrWhiteSpace(message.TextBody) && string.IsNullOrWhiteSpace(message.HtmlBody))
            throw new EmailValidationException(providerName, "Either TextBody or HtmlBody must be provided.");
    }
}
