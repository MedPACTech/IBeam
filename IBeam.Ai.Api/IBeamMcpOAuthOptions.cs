namespace IBeam.Ai;

public sealed class IBeamMcpOAuthOptions
{
    public const string SectionName = "IBeam:Ai:Mcp:OAuth";

    public bool Enabled { get; set; }

    public string ResourceUri { get; set; } = string.Empty;

    public string AuthorizationServerUri { get; set; } = string.Empty;

    public string ResourceName { get; set; } = "IBeam MCP";

    public string[] SupportedScopes { get; set; } = ["tool:mcp"];
}
