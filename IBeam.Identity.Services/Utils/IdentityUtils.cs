using System.Text.RegularExpressions;
using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Models;

namespace IBeam.Identity.Services.Utils;

public static class IdentityUtils
{
    public static readonly Regex EmailRegex = new(@"^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static readonly Regex PhoneRegex = new(@"^\+?[0-9 .-]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (SenderChannel channel, string normalized) NormalizeDestination(string destination)
    {
        destination = destination.Trim();
        if (EmailRegex.IsMatch(destination))
        {
            return (SenderChannel.Email, destination.ToUpperInvariant());
        }
        else if (PhoneRegex.IsMatch(destination))
        {
            var normalized = destination.Replace("-", "").Replace(".", "").Replace(" ", "");
            if (normalized.StartsWith("+01"))
                normalized = normalized.Substring(3);
            else if (normalized.StartsWith("+"))
                normalized = normalized.Substring(1);
            return (SenderChannel.Sms, normalized);
        }
        else
        {
            throw new IdentityValidationException("Destination must be a valid email or phone number.");
        }
    }

    public static void ThrowIfNullOrWhiteSpace(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new IdentityValidationException($"{paramName} is required.");
    }
}
