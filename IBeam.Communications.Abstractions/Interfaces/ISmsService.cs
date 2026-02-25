using IBeam.Communications.Abstractions.Options;

namespace IBeam.Communications.Abstractions;

public interface ISmsService
{
    Task SendAsync(SmsMessage message, SmsOptions? options = null, CancellationToken ct = default);

    async Task SendAsync(
        string to,
        string body,
        SmsOptions? options = null,
        CancellationToken ct = default)
    {
        var message = new SmsMessage
        {
            Body = body
        };
        message.To.Add(to);

        await SendAsync(message, options, ct);
    }
}

