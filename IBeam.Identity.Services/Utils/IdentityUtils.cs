using System.Text.RegularExpressions;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Utils;

public static class IdentityUtils
{
    public static readonly Regex EmailRegex = new(@"^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static readonly Regex PhoneRegex = new(@"^\+?[0-9 ().-]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (SenderChannel channel, string normalized) NormalizeDestination(string destination)
    {
        destination = destination.Trim();
        if (EmailRegex.IsMatch(destination))
        {
            return (SenderChannel.Email, destination.ToUpperInvariant());
        }
        else if (PhoneRegex.IsMatch(destination))
        {
            return (SenderChannel.Sms, NormalizePhoneNumber(destination));
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

    public static string NormalizePhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var trimmed = phone.Trim();
        var hasLeadingPlus = trimmed.StartsWith("+", StringComparison.Ordinal);
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            throw new IdentityValidationException("Phone number is required.");

        if (digits.StartsWith("01", StringComparison.Ordinal) && digits.Length == 12)
            digits = digits[2..];

        if (digits.Length == 10)
            return $"+1{digits}";

        if (digits.Length == 11 && digits.StartsWith("1", StringComparison.Ordinal))
            return $"+{digits}";

        if (hasLeadingPlus && digits.Length >= 8 && digits.Length <= 15)
            return $"+{digits}";

        if (digits.Length >= 8 && digits.Length <= 15)
            return $"+{digits}";

        throw new IdentityValidationException("Destination must be a valid email or phone number.");
    }
}
