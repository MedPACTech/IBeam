namespace IBeam.Communications.Email.Abstractions;

/// <summary>
/// Transport model used by apps/services to send email.
/// From is optional (provider can supply a configured default).
/// </summary>
public sealed record EmailMessage(
    IReadOnlyList<EmailAddress> To,
    string Subject,
    string? TextBody = null,
    string? HtmlBody = null,
    EmailAddress? From = null
);
