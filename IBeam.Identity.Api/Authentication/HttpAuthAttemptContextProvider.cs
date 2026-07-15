using System.Diagnostics;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Http;

namespace IBeam.Identity.Api.Authentication;

public sealed class HttpAuthAttemptContextProvider : IAuthAttemptContextProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpAuthAttemptContextProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public AuthAttemptContext GetCurrent()
    {
        var http = _httpContextAccessor.HttpContext;
        if (http is null)
            return new AuthAttemptContext(CorrelationId: Activity.Current?.Id);

        var request = http.Request;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["path"] = request.Path.Value ?? string.Empty,
            ["method"] = request.Method,
            ["traceIdentifier"] = http.TraceIdentifier
        };

        AddIfPresent(metadata, "host", request.Host.Value);
        AddIfPresent(metadata, "referer", Header(request, "Referer"));

        return new AuthAttemptContext(
            IpAddress: ResolveIpAddress(http),
            UserAgent: Header(request, "User-Agent"),
            DeviceId: Header(request, "X-Device-Id"),
            Country: FirstPresent(Header(request, "CF-IPCountry"), Header(request, "X-Country")),
            Region: Header(request, "X-Region"),
            City: Header(request, "X-City"),
            CorrelationId: FirstPresent(
                Header(request, "X-Correlation-Id"),
                Activity.Current?.Id,
                http.TraceIdentifier),
            Metadata: metadata);
    }

    private static string? ResolveIpAddress(HttpContext http)
    {
        var request = http.Request;
        var forwardedFor = Header(request, "X-Forwarded-For");
        var firstForwarded = forwardedFor?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return FirstPresent(
            firstForwarded,
            Header(request, "CF-Connecting-IP"),
            Header(request, "X-Real-IP"),
            http.Connection.RemoteIpAddress?.ToString());
    }

    private static string? Header(HttpRequest request, string name)
        => request.Headers.TryGetValue(name, out var values)
            ? FirstPresent(values.Select(x => x).ToArray())
            : null;

    private static string? FirstPresent(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static void AddIfPresent(IDictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            metadata[key] = value.Trim();
    }
}
