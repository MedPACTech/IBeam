using IBeam.Communications.Abstractions;

namespace IBeam.Communications.Core.Policies;

public static class Normalization
{
    public static string? NormalizeEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    public static string? NormalizePhone(string? phone)
        => string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(); // later: E.164 formatting

    public static string TrimOrNull(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}