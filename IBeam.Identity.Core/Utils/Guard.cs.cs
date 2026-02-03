namespace IBeam.Identity.Core.Utilities;

public static class Guard
{
    public static string NotNullOrWhiteSpace(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);

        return value;
    }
}
