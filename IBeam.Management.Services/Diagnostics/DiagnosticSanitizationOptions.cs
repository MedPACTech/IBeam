using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.Diagnostics;
/// <summary>Nullable binding makes a blank value equivalent to the safe default; only false enables raw text.</summary>
public sealed class DiagnosticSanitizationOptions
{
    public bool? Enabled { get; set; } = true;
    public bool IsEnabled => Enabled != false;
}
public static class DiagnosticSanitizationPolicy
{
    internal static IConfiguration Section(IConfiguration configuration, string name)
    {
        var section = configuration.GetSection(name);
        if (!string.IsNullOrWhiteSpace(section["Sanitization:Enabled"])) return section;
        return new ConfigurationBuilder().AddConfiguration(section)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sanitization:Enabled"] = null }).Build();
    }
    public static bool Require(DiagnosticSanitizationOptions? options, ManagementDiagnosticsOptions deployment, IHostEnvironment? host)
    {
        var sanitized = options?.IsEnabled != false;
        if (!sanitized && (!NonProductionHost(host?.EnvironmentName) || !NonProductionDeployment(deployment.Environment)))
            throw new InvalidOperationException("Unsanitized diagnostics require an explicitly non-Production host and deployment.");
        return sanitized;
    }
    private static bool NonProductionHost(string? name) => name is not null && new[] { "Development", "Testing", "Staging" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    public static bool NonProductionDeployment(string? name) => name is not null && new[] { "Development", "Dev", "Testing", "Test", "Staging", "QA", "Local" }.Contains(name, StringComparer.OrdinalIgnoreCase);
}
internal sealed class ErrorSanitizationStartup(IOptions<SystemErrorReaderOptions> options, IOptions<ManagementDiagnosticsOptions> deployment,
    ILogger<ErrorSanitizationStartup> logger, IHostEnvironment? host = null) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        if (!DiagnosticSanitizationPolicy.Require(options.Value.Sanitization, deployment.Value, host))
            logger.LogWarning("System error sanitization is disabled; authorized responses include unsanitized diagnostic text.");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
internal sealed class LogSanitizationStartup(IOptions<SystemLogReaderOptions> options, IOptions<ManagementDiagnosticsOptions> deployment,
    ILogger<LogSanitizationStartup> logger, IHostEnvironment? host = null) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        if (!DiagnosticSanitizationPolicy.Require(options.Value.Sanitization, deployment.Value, host))
            logger.LogWarning("System log sanitization is disabled; authorized responses include unsanitized diagnostic text.");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
