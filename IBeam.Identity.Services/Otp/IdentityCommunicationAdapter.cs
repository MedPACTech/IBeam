using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Communications.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Identity.Services.Otp;

public class IdentityCommunicationAdapter : IIdentityCommunicationSender
{
    private readonly IEmailService? _emailService;
    private readonly ISmsService? _smsService;
    private readonly ITemplatedEmailService? _templatedEmailService;

    public IdentityCommunicationAdapter(IServiceProvider sp)
    {
        _emailService = sp.GetService<IEmailService>();
        _smsService = sp.GetService<ISmsService>();
        _templatedEmailService = sp.GetService<ITemplatedEmailService>();
    }

    public async Task SendAsync(IdentitySenderMessage message, CancellationToken ct = default)
    {
        if (message.Channel == SenderChannel.Email)
        {
            if (!string.IsNullOrWhiteSpace(message.Metadata?["TemplateKey"]?.ToString()) && _templatedEmailService != null)
            {
                // Use templated email
                //await _templatedEmailService.SendAsync(
                //    to: message.Destination,
                //    templateKey: message.Metadata["TemplateKey"].ToString(),
                //    model: message.Metadata?["TemplateModel"] ?? new { Code = message.Code, Purpose = message.Purpose, TenantId = message.TenantId },
                //    subject: message.Subject,
                //    options: null,
                //    ct: ct);
                throw new NotImplementedException("Templated email sending is not implemented yet.");
            }
            else if (_emailService != null)
            {
                // Use plain email
                await _emailService.SendAsync(
                    to: message.Destination,
                    subject: message.Subject ?? $"Your OTP Code for {message.Purpose}",
                    textBody: message.Body ?? $"Your code is: {message.Code}",
                    options: null,
                    ct: ct);
            }
            else
            {
                throw new InvalidOperationException("No email service is configured.");
            }
        }
        else if (message.Channel == SenderChannel.Sms)
        {
            if (_smsService == null)
                throw new InvalidOperationException("SMS service is not configured.");

            await _smsService.SendAsync(
                to: message.Destination,
                body: message.Body ?? $"Your code is: {message.Code}",
                options: null,
                ct: ct);
        }
        else
        {
            throw new NotSupportedException($"Unsupported channel: {message.Channel}");
        }
    }
}
