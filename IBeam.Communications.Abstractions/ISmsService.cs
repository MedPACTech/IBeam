namespace IBeam.Communications.Abstractions;

public interface ISmsService
{
    Task SendAsync(SmsMessage message, SmsSendOptions? options = null, CancellationToken ct = default);
}
