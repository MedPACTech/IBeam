namespace IBeam.Communications.Abstractions;

public interface ITemplatedEmailService
{
    Task SendTemplatedEmailAsync(
        IReadOnlyCollection<string> to,
        string subject,
        string templateName,
        object? model = null,
        EmailOptions? options = null,
        CancellationToken ct = default);
}
