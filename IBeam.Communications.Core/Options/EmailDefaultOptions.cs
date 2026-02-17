namespace IBeam.Communications.Core.Options;

public sealed class EmailDefaultsOptions
{
    public const string SectionName = "IBeam:Communications:Email:Defaults";

    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }
}
