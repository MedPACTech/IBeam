namespace IBeam.Communications.Abstractions;

public sealed class EmailTemplateOptions
{
    public const string SectionName = "IBeam:EmailTemplating";
    public string? BasePath { get; set; } // required
    public string? HtmlExtension { get; set; } = ".html";
    public string? TextExtension { get; set; } = ".txt";
}

