namespace IBeam.Management.Repositories.AzureTable;

public sealed class ManagementCredentialStorageOptions
{
    public const string SectionName = "IBeam:Management:Credentials:Storage";
    public string ConnectionString { get; set; } = "";
    public string TableName { get; set; } = "ManagementApiCredentials";
    public override string ToString() => "Management credential storage (connection redacted)";
}
