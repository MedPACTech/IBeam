namespace IBeam.Communications.Email.AzureCommunications;

internal static class AzureCommunicationsConnectionStringValidator
{
    public const string FailureMessage =
        "Configuration validation failed for provider AzureCommunications. " +
        AzureCommunicationsEmailOptions.ConnectionStringConfigurationKey +
        " requires an Azure Communication Services connection string in the form " +
        "endpoint=https://<resource>.communication.azure.com/;accesskey=<key>. " +
        "Do not use Azure Storage/Table, SQL/database, or placeholder values.";

    private static readonly HashSet<string> StorageKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DefaultEndpointsProtocol",
        "AccountName",
        "AccountKey",
        "TableEndpoint",
        "BlobEndpoint",
        "QueueEndpoint",
        "FileEndpoint"
    };

    private static readonly HashSet<string> DatabaseKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Server",
        "Database",
        "Data Source",
        "Initial Catalog",
        "User ID",
        "Uid"
    };

    public static bool IsValid(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        var trimmed = connectionString.Trim();
        if (string.Equals(trimmed, "<AZURE_COMMUNICATIONS_CONNECTION_STRING>", StringComparison.OrdinalIgnoreCase))
            return false;

        var values = ParseConnectionString(trimmed);
        if (values.Count == 0)
            return false;

        if (values.Keys.Any(StorageKeys.Contains) || values.Keys.Any(DatabaseKeys.Contains))
            return false;

        if (!values.TryGetValue("endpoint", out var endpoint) ||
            !values.TryGetValue("accesskey", out var accessKey) ||
            string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(accessKey))
            return false;

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var endpointUri))
            return false;

        return string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               endpointUri.Host.EndsWith(".communication.azure.com", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseConnectionString(string connectionString)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
                values[key] = value;
        }

        return values;
    }
}
