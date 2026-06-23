namespace IBeam.Ai;

public static class AgentToolSchemas
{
    public static object EmptyObject()
        => Object(new Dictionary<string, object>(), Array.Empty<string>());

    public static object Object(
        IReadOnlyDictionary<string, object> properties,
        IReadOnlyCollection<string> required)
        => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };

    public static object String(string description)
        => new Dictionary<string, object>
        {
            ["type"] = "string",
            ["description"] = description
        };

    public static object Boolean(string description)
        => new Dictionary<string, object>
        {
            ["type"] = "boolean",
            ["description"] = description
        };

    public static object Integer(string description)
        => new Dictionary<string, object>
        {
            ["type"] = "integer",
            ["description"] = description
        };

    public static object StringArray(string description)
        => new Dictionary<string, object>
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = new Dictionary<string, object>
            {
                ["type"] = "string"
            }
        };
}
