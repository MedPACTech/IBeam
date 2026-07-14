using IBeam.Api.Abstractions;
using IBeam.Communications.Abstractions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Otp;

public class IdentityCommunicationAdapter : IIdentityCommunicationSender
{
    private readonly IServiceProvider _sp;
    private readonly IApiErrorSink? _errorSink;
    private readonly ILogger<IdentityCommunicationAdapter>? _logger;
    private readonly IdentityEmailTemplateOptions _templateOptions;

    public IdentityCommunicationAdapter(IServiceProvider sp)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _errorSink = sp.GetService<IApiErrorSink>();
        _logger = sp.GetService<ILogger<IdentityCommunicationAdapter>>();
        _templateOptions = sp.GetService<IOptions<IdentityEmailTemplateOptions>>()?.Value ?? new IdentityEmailTemplateOptions();
    }

    public async Task SendAsync(IdentitySenderMessage message, CancellationToken ct = default)
    {
        try
        {
            await SendCoreAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await PersistCommunicationErrorAsync(message, ex, ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task SendCoreAsync(IdentitySenderMessage message, CancellationToken ct)
    {
        if (message.Channel == SenderChannel.Email)
        {
            var sentWithTemplate = await TrySendTemplatedEmailAsync(message, ct).ConfigureAwait(false);
            if (!sentWithTemplate)
            {
                var emailService = _sp.GetService<IEmailService>();
                if (emailService is null)
                    throw new EmailConfigurationException("No email service is configured.");

                await emailService.SendAsync(
                    to: message.Destination,
                    subject: message.Subject ?? $"Your OTP Code for {message.Purpose}",
                    textBody: message.Body ?? $"Your code is: {message.Code}",
                    options: null,
                    ct: ct).ConfigureAwait(false);
            }
        }
        else if (message.Channel == SenderChannel.Sms)
        {
            var smsService = _sp.GetService<ISmsService>();
            if (smsService is null)
                throw new SmsConfigurationException("SMS service is not configured.");

            await smsService.SendAsync(
                to: message.Destination,
                body: message.Body ?? $"Your code is: {message.Code}",
                options: null,
                ct: ct).ConfigureAwait(false);
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

        var templatedEmailService = _sp.GetService<ITemplatedEmailService>();
        if (templatedEmailService is null)
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
            await templatedEmailService.SendTemplatedEmailAsync(
                to: new[] { message.Destination },
                subject: subject,
                templateName: templateName,
                model: model,
                options: null,
                ct: ct).ConfigureAwait(false);

            return true;
        }
        catch when (_templateOptions.FallbackToPlainIfMissingTemplate)
        {
            return false;
        }
    }

    private async Task PersistCommunicationErrorAsync(IdentitySenderMessage message, Exception ex, CancellationToken ct)
    {
        if (_errorSink is null)
            return;

        try
        {
            await _errorSink.SaveAsync(new ApiErrorRecord
            {
                Source = nameof(IdentityCommunicationAdapter),
                Path = $"IdentityCommunication/{message.Channel}",
                Method = "SEND",
                Message = BuildErrorMessage(message, ex),
                Exception = ex.ToString(),
                TraceId = ResolveTraceId(message),
                Timestamp = DateTimeOffset.UtcNow
            }, ct).ConfigureAwait(false);

            ex.Data[SystemErrorLogKeys.AlreadyPersisted] = true;
        }
        catch (Exception logEx) when (logEx is not OperationCanceledException)
        {
            _logger?.LogWarning(logEx, "Failed to persist communication error for {Channel}.", message.Channel);
        }
    }

    private static string BuildErrorMessage(IdentitySenderMessage message, Exception ex)
    {
        var parts = new List<string>
        {
            "Identity communication send failed.",
            $"Channel={message.Channel}",
            $"Destination={Sanitize(message.Destination)}"
        };

        if (message.Purpose.HasValue)
            parts.Add($"Purpose={message.Purpose.Value}");

        if (message.TenantId.HasValue)
            parts.Add($"TenantId={message.TenantId.Value:D}");

        parts.Add($"Error={Sanitize(ex.Message)}");
        return string.Join("; ", parts);
    }

    private static string ResolveTraceId(IdentitySenderMessage message)
    {
        if (message.Metadata is not null)
        {
            foreach (var key in new[] { "traceId", "TraceId", "correlationId", "CorrelationId" })
            {
                if (message.Metadata.TryGetValue(key, out var value) &&
                    value is not null &&
                    !string.IsNullOrWhiteSpace(value.ToString()))
                {
                    return value.ToString()!;
                }
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string Sanitize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "<empty>"
            : string.Join(' ', value.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));

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
