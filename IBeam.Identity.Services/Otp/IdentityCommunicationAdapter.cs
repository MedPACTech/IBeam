using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Communications.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Otp;

public class IdentityCommunicationAdapter : IIdentityCommunicationSender
{
    private readonly IEmailService? _emailService;
    private readonly ISmsService? _smsService;
    private readonly ITemplatedEmailService? _templatedEmailService;
    private readonly IdentityEmailTemplateOptions _templateOptions;

    public IdentityCommunicationAdapter(IServiceProvider sp)
    {
        _emailService = sp.GetService<IEmailService>();
        _smsService = sp.GetService<ISmsService>();
        _templatedEmailService = sp.GetService<ITemplatedEmailService>();
        _templateOptions = sp.GetService<IOptions<IdentityEmailTemplateOptions>>()?.Value ?? new IdentityEmailTemplateOptions();
    }

    public async Task SendAsync(IdentitySenderMessage message, CancellationToken ct = default)
    {
        if (message.Channel == SenderChannel.Email)
        {
            var sentWithTemplate = await TrySendTemplatedEmailAsync(message, ct);
            if (!sentWithTemplate)
            {
                if (_emailService is null)
                    throw new InvalidOperationException("No email service is configured.");

                await _emailService.SendAsync(
                    to: message.Destination,
                    subject: message.Subject ?? $"Your OTP Code for {message.Purpose}",
                    textBody: message.Body ?? $"Your code is: {message.Code}",
                    options: null,
                    ct: ct);
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

    private async Task<bool> TrySendTemplatedEmailAsync(IdentitySenderMessage message, CancellationToken ct)
    {
        if (!_templateOptions.Enabled)
            return false;

        if (_templatedEmailService is null)
        {
            if (_templateOptions.FallbackToPlainIfMissingTemplate)
                return false;
            throw new InvalidOperationException("Templated email is enabled but no templated email service is registered.");
        }

        var configured = _templateOptions.TryGetTemplate(message.Purpose, out var definition);
        var templateName = configured
            ? definition.TemplateName
            : (_templateOptions.UseTemplatesForAllEmail && message.Purpose.HasValue ? message.Purpose.Value.ToString() : null);

        if (string.IsNullOrWhiteSpace(templateName))
            return false;

        var subject = configured && !string.IsNullOrWhiteSpace(definition.Subject)
            ? definition.Subject!
            : (message.Subject ?? $"Notification: {message.Purpose}");

        var model = BuildTemplateModel(message, _templateOptions);

        try
        {
            await _templatedEmailService.SendTemplatedEmailAsync(
                to: new[] { message.Destination },
                subject: subject,
                templateName: templateName,
                model: model,
                options: null,
                ct: ct);

            return true;
        }
        catch when (_templateOptions.FallbackToPlainIfMissingTemplate)
        {
            return false;
        }
    }

    private static Dictionary<string, object?> BuildTemplateModel(IdentitySenderMessage message, IdentityEmailTemplateOptions options)
    {
        var expiresInMinutes = message.ExpiresAt.HasValue
            ? Math.Max(0, (int)Math.Ceiling((message.ExpiresAt.Value - DateTimeOffset.UtcNow).TotalMinutes))
            : (int?)null;

        var expiresAtValue = options.ExpirationDisplay == ExpirationDisplayMode.MinutesRemaining
            ? (object?)expiresInMinutes
            : message.ExpiresAt?.ToString("O");

        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Destination"] = message.Destination,
            ["Code"] = message.Code,
            ["Subject"] = message.Subject,
            ["Body"] = message.Body,
            ["Name"] = message.Name,
            ["Purpose"] = message.Purpose?.ToString(),
            ["TenantId"] = message.TenantId?.ToString("D"),
            ["ExpiresAt"] = expiresAtValue,
            ["ExpiresAtUtc"] = message.ExpiresAt?.ToString("O"),
            ["ExpiresInMinutes"] = expiresInMinutes
        };

        if (message.Metadata is not null)
        {
            foreach (var kv in message.Metadata)
                model[kv.Key] = kv.Value;
        }

        return model;
    }
}
