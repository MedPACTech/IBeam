namespace IBeam.Communications.Sms.AzureCommunications;

public sealed class AzureCommunicationsSmsOptions
{
    public const string SectionName = "IBeam:Communications:Sms:Providers:AzureCommunications";
    public string ConnectionString { get; set; } = string.Empty;
}
