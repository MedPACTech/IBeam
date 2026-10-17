using IBeam.Services.Abstractions;
using Microsoft.AspNetCore.Http;

namespace IBeam.Management.Diagnostics.Api.Authentication;

/// <summary>Server context only. Never copies request headers, query strings, user agents or credentials into audit.</summary>
internal sealed class ManagementCredentialAuditContext(IHttpContextAccessor http) : IAuditActorProvider, IAuditRequestContextProvider
{
    public string? GetActorId()
    {
        var identity = http.HttpContext?.User;
        if (identity?.FindFirst(ManagementClaimTypes.SubjectType)?.Value != ManagementSubjectTypes.Credential) return null;
        return Guid.TryParse(identity.FindFirst(ManagementClaimTypes.CredentialId)?.Value, out var id)
            ? "management-credential:" + id.ToString("D") : null;
    }
    public AuditRequestContext GetContext() => new()
    {
        CorrelationId = http.HttpContext?.TraceIdentifier,
        IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString()
    };
}
