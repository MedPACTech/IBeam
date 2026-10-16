using System.Text.Json;
using IBeam.Management.Models;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.Diagnostics;

public sealed record PortalDiagnosticState(string TargetToken, SystemLogQuery Query, DateTimeOffset ExpiresUtc);
public sealed class PortalDiagnosticTokens
{
    private readonly DiagnosticTokenCodec _codec;
    public PortalDiagnosticTokens(IOptions<PortalDiagnosticsOptions> options)
    {
        if (!options.Value.IsValid()) throw new InvalidOperationException("Valid portal diagnostics settings are required.");
        _codec = new DiagnosticTokenCodec(Convert.FromBase64String(options.Value.ProtectionKey));
    }
    private static string Context(ManagementDiagnosticConnection c, string permission) => JsonSerializer.Serialize(new {
        c.OrganizationId, c.UserId, c.ApplicationId, c.ApplicationKey, c.Environment, c.BaseUrl, c.CredentialId, c.Version, permission });
    public string Protect(ManagementDiagnosticConnection c, string permission, string purpose, PortalDiagnosticState state)
    {
        ApplicationDiagnosticsClient.CheckOpaque(state.TargetToken);
        return _codec.Protect("portal-" + purpose, Context(c, permission), state);
    }
    public PortalDiagnosticState Unprotect(ManagementDiagnosticConnection c, string permission, string purpose, string token, DateTimeOffset now)
    {
        var state = _codec.Unprotect<PortalDiagnosticState>("portal-" + purpose, Context(c, permission), token);
        if (state.Query is null || state.ExpiresUtc <= now) throw new DiagnosticTokenException();
        ApplicationDiagnosticsClient.CheckOpaque(state.TargetToken);
        return state;
    }
}
