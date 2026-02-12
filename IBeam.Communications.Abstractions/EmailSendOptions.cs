namespace IBeam.Communications.Abstractions;

public sealed record EmailSendOptions
{
    /// <summary>
    /// If true and EmailMessage.From is null, the provider may use its configured default sender.
    /// Defaults to true.
    /// </summary>
    public bool UseDefaultFromIfMissing { get; init; } = true;

    /// <summary>
    /// Optional per-call sender override. Wins over message.From and provider defaults.
    /// </summary>
    public EmailAddress? FromOverride { get; init; }
}
