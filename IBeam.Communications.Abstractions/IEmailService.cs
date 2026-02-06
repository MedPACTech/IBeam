namespace IBeam.Communications.Email.Abstractions;

public interface IEmailService
{
    Task SendAsync(EmailMessage message, EmailSendOptions? options = null, CancellationToken ct = default);
}
