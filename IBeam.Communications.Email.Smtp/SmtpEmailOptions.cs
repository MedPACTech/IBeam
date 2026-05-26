namespace IBeam.Communications.Email.Smtp;

public sealed class SmtpEmailOptions
{
    // Config section: IBeam:Communications:Email:Smtp
    public const string SectionName = "IBeam:Communications:Email:Smtp";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    // If true, uses machine credentials (Windows/AD scenarios)
    public bool UseDefaultCredentials { get; set; } = false;

    // Optional credentials (common for SMTP relays)
    public string? Username { get; set; }
    public string? Password { get; set; }

    // Default sender (used when EmailMessage.From is null and options allow default)
    public string DefaultFromAddress { get; set; } = "noreply@localhost";
    public string? DefaultFromDisplayName { get; set; }
}
