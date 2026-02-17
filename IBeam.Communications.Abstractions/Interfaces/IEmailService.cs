namespace IBeam.Communications.Abstractions;

public interface IEmailService
{
    Task SendAsync(EmailMessage message, EmailSendOptions? options = null, CancellationToken ct = default);

    async Task SendAsync(
        string to,
        string subject,
        string? htmlBody = null,
        string? textBody = null,
        EmailSendOptions? options = null,
        CancellationToken ct = default)
    {
        var message = new EmailMessage
        {
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = textBody
        };
        message.To.Add(to);

        await SendAsync(message, options, ct);
    }
}


