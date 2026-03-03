using System.Text.RegularExpressions;

namespace IBeam.Communications.Abstractions;

public sealed class EmailOptions
{
    public const string SectionName = "IBeam:Communications:Email";

    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(FromAddress))
            return false;

        //TODO: move to Util functions project, make compiled
        // Simple email validation: must have '@' and a domain part
        return Regex.IsMatch(FromAddress, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }
}