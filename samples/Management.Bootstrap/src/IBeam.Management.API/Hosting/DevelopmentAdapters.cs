using System.Net;
using System.Net.Mail;
using System.Text.Json;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.DataProtection;

namespace IBeam.Management.API.Hosting;

// Host-only local testing adapters. These are never packaged as framework implementations.
public sealed class DevelopmentSecretStore : IManagementSecretStore
{
    private readonly string _directory;
    private readonly IDataProtectionProvider _protection;
    public bool SupportsWrites => true;
    public DevelopmentSecretStore(IConfiguration configuration, IHostEnvironment environment, IDataProtectionProvider protection)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Development secret storage is unavailable.");
        _directory = Path.Combine(HostRegistration.DataDirectory(configuration), "connection-secrets");
        Directory.CreateDirectory(_directory);
        _protection = protection;
    }
    public async Task<string> CreateAsync(ManagementSecretScope scope, ManagementSecretValue secret, CancellationToken ct = default)
    {
        var reference = Guid.NewGuid().ToString("N");
        var bytes = System.Text.Encoding.UTF8.GetBytes(Protector(scope).Protect(secret.Value));
        await using var stream = new FileStream(Path.Combine(_directory, reference), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await stream.WriteAsync(bytes, ct);
        return reference;
    }
    public async Task<ManagementSecretValue> ReadAsync(ManagementSecretScope scope, string reference, CancellationToken ct = default)
    {
        if (!Guid.TryParseExact(reference, "N", out _)) throw new ManagementSecretStoreUnavailableException();
        try { return new(Protector(scope).Unprotect(await File.ReadAllTextAsync(Path.Combine(_directory, reference), ct))); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { throw new ManagementSecretStoreUnavailableException(); }
    }
    private IDataProtector Protector(ManagementSecretScope scope)
        => _protection.CreateProtector("ManagementConnection", scope.OrganizationId.ToString("N"), scope.ApplicationId.ToString("N"));
}

public sealed class DevelopmentMailSender(IConfiguration configuration, IHostEnvironment environment) : IIdentityCommunicationSender
{
    public async Task SendAsync(IdentitySenderMessage message, CancellationToken ct = default)
    {
        if (!environment.IsDevelopment() || message.Channel != SenderChannel.Email)
            throw new InvalidOperationException("Local email delivery is unavailable.");
        var directory = Path.Combine(HostRegistration.DataDirectory(configuration), "outbox");
        Directory.CreateDirectory(directory);
        // Private operator files only: never HTTP-accessible and never written to application logs.
        var body = JsonSerializer.Serialize(new
        {
            message.Destination, message.Code, message.Subject, message.Body, message.ExpiresAt, message.Metadata,
            Purpose = message.Purpose?.ToString(), CreatedUtc = DateTimeOffset.UtcNow
        });
        await File.WriteAllTextAsync(Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json"), body, ct);
    }
}

public sealed class SmtpIdentitySender(IConfiguration configuration) : IIdentityCommunicationSender
{
    public async Task SendAsync(IdentitySenderMessage message, CancellationToken ct = default)
    {
        if (message.Channel != SenderChannel.Email) throw new InvalidOperationException("Only email delivery is configured.");
        var section = configuration.GetSection("ManagementHost:Mail:Smtp");
        using var smtp = new SmtpClient(section["Host"] ?? throw new InvalidOperationException("SMTP host is required."), section.GetValue("Port", 587))
        {
            EnableSsl = true, UseDefaultCredentials = false
        };
        if (!string.IsNullOrWhiteSpace(section["Username"])) smtp.Credentials = new NetworkCredential(section["Username"], section["Password"]);
        using var mail = new MailMessage(section["From"] ?? throw new InvalidOperationException("SMTP sender is required."), message.Destination)
        {
            Subject = message.Subject ?? "Support sign-in",
            Body = message.Purpose == SenderPurpose.TenantInvitation || string.IsNullOrWhiteSpace(message.Code) ? message.Body ?? "" : $"Your support sign-in code is {message.Code}. Expires: {message.ExpiresAt:O}.",
            IsBodyHtml = false
        };
        try { await smtp.SendMailAsync(mail, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { throw new InvalidOperationException("Support email delivery failed."); }
    }
}
