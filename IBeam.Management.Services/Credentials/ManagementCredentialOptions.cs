namespace IBeam.Management.Services.Credentials;

public sealed class ManagementCredentialProvisioningOptions
{
    public const string SectionName = "IBeam:Management:Credentials:Provisioning";
    public bool Enabled { get; set; }
    public int MaximumLifetimeDays { get; set; } = 90;
}

internal static class ManagementCredentialPolicy
{
    public static bool ValidCapabilities(IReadOnlyList<string>? capabilities)
        => global::IBeam.Management.Operations.ManagementOperationRegistry.BuiltIn.ValidCapabilities(capabilities);
}
