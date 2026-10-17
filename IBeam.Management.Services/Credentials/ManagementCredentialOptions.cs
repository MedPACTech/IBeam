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
        => capabilities is { Count: > 0 and <= 3 } &&
           capabilities.Distinct(StringComparer.Ordinal).Count() == capabilities.Count &&
           capabilities.All(c => c is ManagementOperations.DiagnosticsInfo or ManagementOperations.ErrorsRead or ManagementOperations.LogsRead);
}
