namespace IBeam.Communications.Email.AzureCommunications;

public sealed class AzureCommunicationsEmailOptions
{
    public const string SectionName = "IBeam:Communications:Email:Providers:AzureCommunications";
    public string ConnectionString { get; set; } = string.Empty;
}
