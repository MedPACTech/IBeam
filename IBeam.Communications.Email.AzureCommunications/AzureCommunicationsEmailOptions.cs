namespace IBeam.Communications.Email.AzureCommunications;

public sealed class AzureCommunicationsEmailOptions
{
    // Config section: IBeam:Communications:Email:AzureCommunications
    public const string SectionName = "IBeam:Communications:Email:AzureCommunications";

    /// <summary>Azure Communication Services connection string.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Default sender address (must be a verified/approved sender in ACS).
    /// Used when EmailMessage.From is null and send options allow default sender.
    /// </summary>
    public string DefaultFromAddress { get; set; } = "";

    public string? DefaultFromDisplayName { get; set; }
}
