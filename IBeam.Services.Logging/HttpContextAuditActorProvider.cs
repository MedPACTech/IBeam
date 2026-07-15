using IBeam.Services.Abstractions;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace IBeam.Services.Logging;

public sealed class HttpContextAuditActorProvider :
    IAuditActorProvider,
    IAuditRequestContextProvider,
    IServiceOperationPrincipalProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextAuditActorProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetActorId()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal is null || principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? principal.Identity?.Name;
    }

    public AuditRequestContext GetContext()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
        {
            return new AuditRequestContext();
        }

        return new AuditRequestContext
        {
            CorrelationId = context.TraceIdentifier,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = context.Request.Headers["User-Agent"].ToString(),
            DeviceId = context.Request.Headers.TryGetValue("X-Device-Id", out var deviceId)
                ? deviceId.ToString()
                : null
        };
    }

    public ClaimsPrincipal? GetPrincipal()
        => _httpContextAccessor.HttpContext?.User;
}
