namespace IBeam.Communications.Email.PickupDirectory;

public sealed class PickupDirectoryEmailOptions
{
    public const string SectionName = "IBeam:Communications:Email:PickupDirectory";

    public string DirectoryPath { get; set; } = "";

    public string DefaultFromAddress { get; set; } = "";
    public string? DefaultFromDisplayName { get; set; }
}
