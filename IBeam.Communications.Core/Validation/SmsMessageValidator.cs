using IBeam.Communications.Abstractions;

namespace IBeam.Communications.Core.Validation;

public static class SmsMessageValidator
{
    public static void Validate(SmsMessage message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));

        if (message.To is null || message.To.Count == 0 || message.To.All(string.IsNullOrWhiteSpace))
            throw new SmsValidationException("SmsMessage.To must contain at least one recipient.");

        if (string.IsNullOrWhiteSpace(message.Body))
            throw new SmsValidationException("SmsMessage.Body is required.");
    }
}
