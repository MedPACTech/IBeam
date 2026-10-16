using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Management.Models;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services.Diagnostics;

/// <summary>Sources written by IBeam itself, each with a fixed summary. Host-approved extras get a generic summary.</summary>
public static class SystemLogSources
{
    public const string Other = "System";
    private static readonly Dictionary<string, string> Builtin = new(StringComparer.Ordinal)
    {
        ["RequestPipeline"] = "An unhandled request processing failure was logged.",
        ["AppDomain.UnhandledException"] = "An unhandled process exception was logged.",
        ["TaskScheduler.UnobservedTaskException"] = "An unobserved background task exception was logged."
    };
    private const string Generic = "A system log entry was recorded.";

    public static bool IsApproved(string source, SystemLogReaderOptions options)
        => Builtin.ContainsKey(source) || options.AllowedSources.Contains(source, StringComparer.Ordinal);

    internal static (string Source, string Summary) Describe(string source, SystemLogReaderOptions options)
        => Builtin.TryGetValue(source, out var summary) ? (source, summary) :
            options.AllowedSources.Contains(source, StringComparer.Ordinal) ? (source, Generic) : (Other, Generic);
}

/// <summary>Fixed source/summary catalog, normalized level and keyed correlation pseudonyms; message/detail text is never copied.</summary>
public sealed class SystemLogProjection
{
    private readonly byte[] _correlationKey;
    private readonly SystemLogReaderOptions _options;
    private readonly ManagementDiagnosticsOptions _deployment;
    public SystemLogProjection(IOptions<SystemLogReaderOptions> options, IOptions<ManagementDiagnosticsOptions> deployment)
    {
        if (!options.Value.IsValid()) throw new InvalidOperationException("Valid system log reader settings are required.");
        _options = options.Value;
        _correlationKey = HMACSHA256.HashData(Convert.FromBase64String(options.Value.ProtectionKey),
            Encoding.UTF8.GetBytes("IBeam.Management.LogCorrelation.Key.v1"));
        _deployment = deployment.Value;
    }

    public SystemLogDto Project(string id, SystemLogMetadata record)
    {
        var (source, summary) = SystemLogSources.Describe(record.Source, _options);
        var level = SystemLogLevels.All.Contains(record.Level, StringComparer.Ordinal) ? record.Level : "Unknown";
        string? correlation = null;
        if (!string.IsNullOrWhiteSpace(record.TraceId) && record.TraceId.Length <= 128)
        {
            var input = JsonSerializer.SerializeToUtf8Bytes(new[]
            {
                "IBeam.Management.LogCorrelation.v1", _deployment.ApplicationKey, _deployment.Environment, record.TraceId
            });
            correlation = "c1_" + Convert.ToHexString(HMACSHA256.HashData(_correlationKey, input)).ToLowerInvariant();
        }
        return new SystemLogDto(id, record.OccurredAtUtc.ToUniversalTime(), level, source, summary, correlation);
    }
}
