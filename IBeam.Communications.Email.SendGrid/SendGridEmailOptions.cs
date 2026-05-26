namespace IBeam.Communications.Email.SendGrid;

public sealed class SendGridEmailOptions
{
    public const string SectionName = "IBeam:Communications:Email:SendGrid";

    /// <summary>SendGrid API key.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Default sender address (recommended: a verified sender/domain).</summary>
    public string DefaultFromAddress { get; set; } = "";

    public string? DefaultFromDisplayName { get; set; }

    /// <summary>
    /// If true, enables SendGrid "sandbox mode" (accepted but not delivered).
    /// Great for staging environments.
    /// </summary>
    public bool SandboxMode { get; set; } = false;
}
