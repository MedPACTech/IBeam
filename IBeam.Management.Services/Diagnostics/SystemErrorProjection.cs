using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Management.Models;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services.Diagnostics;

/// <summary>Fixed safe fields and keyed correlation, with separately marked raw text only after an explicit non-Production policy check.</summary>
public sealed class SystemErrorProjection
{
    private readonly byte[] _correlationKey;
    private readonly IHostEnvironment? _host;
    private readonly SystemErrorReaderOptions _options;
    private readonly ManagementDiagnosticsOptions _deployment;
    public SystemErrorProjection(IOptions<SystemErrorReaderOptions> options, IOptions<ManagementDiagnosticsOptions> deployment)
        : this(options, deployment, null) { }
    public SystemErrorProjection(IOptions<SystemErrorReaderOptions> options, IOptions<ManagementDiagnosticsOptions> deployment, IHostEnvironment? host = null)
    {
        if (!options.Value.IsValid()) throw new InvalidOperationException("Valid system error reader settings are required.");
        _correlationKey = HMACSHA256.HashData(Convert.FromBase64String(options.Value.ProtectionKey),
            Encoding.UTF8.GetBytes("IBeam.Management.ErrorCorrelation.Key.v1"));
        _deployment = deployment.Value;
        _host = host;
        _options = options.Value;
        _ = DiagnosticSanitizationPolicy.Require(_options.Sanitization, _deployment, host);
    }

    public SystemErrorDto Project(string id, SystemErrorRecord record)
    {
        var sanitized = DiagnosticSanitizationPolicy.Require(_options.Sanitization, _deployment, _host);
        var (source, summary) = record.Source switch
        {
            "RequestPipeline" => ("RequestPipeline", "An unexpected request processing error was recorded."),
            _ => ("System", "An unexpected system error was recorded.")
        };
        string? correlation = null;
        if (!string.IsNullOrWhiteSpace(record.TraceId) && record.TraceId.Length <= 128)
        {
            var input = JsonSerializer.SerializeToUtf8Bytes(new[]
            {
                "IBeam.Management.ErrorCorrelation.v1", _deployment.ApplicationKey, _deployment.Environment, record.TraceId
            });
            correlation = "c1_" + Convert.ToHexString(HMACSHA256.HashData(_correlationKey, input)).ToLowerInvariant();
        }
        return new SystemErrorDto(id, record.OccurredAtUtc.ToUniversalTime(), source, summary, correlation)
        {
            Unsanitized = sanitized ? null : new(record.Source, record.TraceId, record.Message, record.Exception, record.Path, record.Method)
        };
    }
}
