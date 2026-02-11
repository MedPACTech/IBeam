namespace IBeam.Communications.Email.Abstractions;

public interface ITemplatedEmailService
{
    Task SendTemplatedEmailAsync(
        string recipient,
        string subject,
        string templateName,
        object? model = null,
        EmailSendOptions? options = null,
        CancellationToken ct = default);
}
