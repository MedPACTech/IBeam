using System.Text.Json;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

internal static class OAuthTableSerialization
{
    public static string Write(IReadOnlyList<string> values) =>
        JsonSerializer.Serialize(values ?? Array.Empty<string>());

    public static IReadOnlyList<string> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }
}
