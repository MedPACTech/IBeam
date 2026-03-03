namespace IBeam.Communications.Abstractions;

/// <summary>
/// Transport model used by apps/services to send email.
/// From is optional (provider can supply a configured default).
/// </summary>
public sealed class EmailMessage
{
    public List<string> To { get; } = new();
    public string Subject { get; set; } = string.Empty;
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
}

