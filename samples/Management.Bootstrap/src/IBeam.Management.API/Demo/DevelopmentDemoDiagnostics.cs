using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;
using Microsoft.AspNetCore.DataProtection;

namespace IBeam.Management.API.Demo;

public sealed record DemoApplication(string ApplicationKey, string Name, string Environment, string BaseUrl, int ErrorCount, int LogCount);

/// <summary>Explicit, synthetic-only local fixture. It opens no target network connection and authenticates no human user.</summary>
public sealed class DevelopmentDemoDiagnostics : IManagementTargetConnectionProbe, IApplicationDiagnosticsClient
{
    private readonly DemoApplication[] _apps;
    private readonly Dictionary<string, string> _keys;
    private readonly IDataProtectionProvider _protection;
    private readonly DateTimeOffset _anchor = DateTimeOffset.UtcNow;
    private readonly TimeProvider _clock = TimeProvider.System;
    private sealed record RecordState(int Index, DateTimeOffset ExpiresUtc);
    private sealed record PageState(int Offset, DateTimeOffset ExpiresUtc);

    public static void Validate(IConfiguration config, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() || config["ManagementHost:Secrets:Provider"] != "DevelopmentFile" ||
            config["ManagementHost:Mail:Provider"] != "DevelopmentOutbox" || config["IBeam:Management:Identity:Storage:Mode"] != "Separate" ||
            config["ManagementHost:Storage:TablePrefix"] != "DemoManagement" || config["IBeam:Management:Identity:Storage:TablePrefix"] != "DemoManagement")
            throw new InvalidOperationException("Synthetic demo mode requires its own Development-only management realm and local adapters.");
        foreach (var name in new[] { "ManagementHost:Storage:ConnectionString", "IBeam:Management:Identity:Storage:ConnectionString" })
        {
            var client = new TableServiceClient(config[name] ?? "");
            if (!client.Uri.IsLoopback || client.AccountName != "devstoreaccount1")
                throw new InvalidOperationException("Synthetic demo mode requires isolated loopback emulator storage.");
        }
    }

    public DevelopmentDemoDiagnostics(IConfiguration config, IHostEnvironment environment, IDataProtectionProvider protection)
    {
        Validate(config, environment);
        _protection = protection;
        _apps = JsonSerializer.Deserialize<DemoApplication[]>(File.ReadAllText(Path.Combine(environment.ContentRootPath, "Demo", "scenario.json")), JsonOptions) ?? [];
        if (_apps.Length != 3 || _apps.Any(x => !x.ApplicationKey.StartsWith("demo-", StringComparison.Ordinal) || x.Environment != "Testing" ||
            !Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.Host.EndsWith(".demo.invalid", StringComparison.Ordinal) || uri.Port != 443 ||
            x.ErrorCount is < 0 or > 100 || x.LogCount is < 0 or > 100))
            throw new InvalidOperationException("The synthetic scenario is invalid.");
        var path = Path.Combine(Hosting.HostRegistration.DataDirectory(config), "demo-keys.json");
        _keys = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        if (_apps.Any(x => !_keys.TryGetValue(x.ApplicationKey, out var key) || key.Length < 32))
            throw new InvalidOperationException("Synthetic fixture credentials have not been initialized.");
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DemoApplication Require(string origin, ManagementSecretValue key)
    {
        var app = _apps.SingleOrDefault(x => x.BaseUrl == origin) ?? throw new ManagementConnectionInvalidException();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key.Value), Encoding.UTF8.GetBytes(_keys[app.ApplicationKey])))
            throw new ManagementAccessDeniedException();
        return app;
    }
    private DemoApplication Require(ManagementDiagnosticConnection connection)
    {
        var app = Require(connection.BaseUrl, connection.Key);
        if (app.ApplicationKey != connection.ApplicationKey || app.Environment != connection.Environment) throw new ManagementAccessDeniedException();
        return app;
    }
    private static ManagementDiagnosticsInfo Info(DemoApplication app)
        => new(app.ApplicationKey, app.Environment, 1, app.ErrorCount == 0
            ? [ManagementOperations.DiagnosticsInfo, ManagementOperations.LogsRead]
            : [ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead]);
    public Task<ManagementDiagnosticsInfo> ProbeAsync(string origin, ManagementSecretValue key, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(Info(Require(origin, key))); }
    public Task<ManagementDiagnosticsInfo> InfoAsync(ManagementDiagnosticConnection connection, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(Info(Require(connection))); }

    public Task<SystemErrorPage> ErrorsAsync(ManagementDiagnosticConnection connection, DiagnosticQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var app = Require(connection);
        if (app.ErrorCount == 0) throw new ManagementAccessDeniedException();
        var q = new SystemLogQuery { FromUtc = query.FromUtc, ToUtc = query.ToUtc, PageSize = query.PageSize, Cursor = query.Cursor };
        var rows = Enumerable.Range(0, app.ErrorCount).Select(i => Error(app, i)).Where(x => Within(x.OccurredAtUtc, q)).ToArray();
        var (items, next) = Page(app, "errors", q, rows);
        return Task.FromResult(new SystemErrorPage(items, q.PageSize ?? 50, next));
    }
    public Task<SystemLogPage> LogsAsync(ManagementDiagnosticConnection connection, SystemLogQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var app = Require(connection);
        if (query.Source is not null && !SystemLogSources.IsApproved(query.Source, new())) throw new DiagnosticQueryException();
        var rows = Enumerable.Range(0, app.LogCount).Where(i => query.TraceId is null || query.TraceId == Trace(app, i))
            .Select(i => Log(app, i)).Where(x => Within(x.OccurredAtUtc, query) && (query.Source is null || x.Source == query.Source) && (query.Level is null || x.Level == query.Level)).ToArray();
        var (items, next) = Page(app, "logs", query, rows);
        return Task.FromResult(new SystemLogPage(items, query.PageSize ?? 50, next));
    }
    public Task<SystemErrorDto> ErrorAsync(ManagementDiagnosticConnection connection, string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var app = Require(connection); var index = ReadRecord(app, "errors", id, app.ErrorCount);
        return Task.FromResult(Error(app, index) with { Id = id });
    }
    public Task<SystemLogDto> LogAsync(ManagementDiagnosticConnection connection, string id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var app = Require(connection); var index = ReadRecord(app, "logs", id, app.LogCount);
        return Task.FromResult(Log(app, index) with { Id = id });
    }
    private (T[] Items, string? Next) Page<T>(DemoApplication app, string kind, SystemLogQuery query, T[] rows)
    {
        var size = query.PageSize ?? 50;
        if (size is < 1 or > 100) throw new DiagnosticQueryException();
        var context = JsonSerializer.Serialize(new { query.FromUtc, query.ToUtc, size, query.Source, query.Level, query.TraceId });
        var saved = query.Cursor is null ? null : Unprotect<PageState>(app, kind, context, query.Cursor);
        if (saved is not null && (saved.ExpiresUtc <= _clock.GetUtcNow() || saved.Offset < 0 || saved.Offset > rows.Length)) throw new DiagnosticTokenException();
        var offset = saved?.Offset ?? 0;
        var items = rows.Skip(offset).Take(size).ToArray();
        var next = offset + items.Length < rows.Length
            ? Protect(app, kind, context, new PageState(offset + items.Length, saved?.ExpiresUtc ?? _clock.GetUtcNow().AddMinutes(15))) : null;
        return (items, next);
    }
    private SystemErrorDto Error(DemoApplication app, int index)
        => new(RecordId(app, "errors", index), _anchor.AddMinutes(-3 - index * 7), index % 2 == 0 ? "RequestPipeline" : "System",
            "Synthetic demo system error.", Correlation(app, index));
    private SystemLogDto Log(DemoApplication app, int index)
        => new(RecordId(app, "logs", index), _anchor.AddMinutes(-2 - index * 5), new[] { "Information", "Warning", "Error", "Critical" }[index % 4],
            new[] { "RequestPipeline", "AppDomain.UnhandledException", "TaskScheduler.UnobservedTaskException" }[index % 3], "Synthetic demo system log.", Correlation(app, index));
    private static string Trace(DemoApplication app, int index) => $"{app.ApplicationKey}-trace-{index:D2}";
    private static string Correlation(DemoApplication app, int index) => "c1_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Trace(app, index)))).ToLowerInvariant();
    private static bool Within(DateTimeOffset at, SystemLogQuery query) => at >= (query.FromUtc ?? DateTimeOffset.MinValue) && at < (query.ToUtc ?? DateTimeOffset.MaxValue);
    private string RecordId(DemoApplication app, string kind, int index) => Protect(app, kind, "record", new RecordState(index, _clock.GetUtcNow().AddMinutes(15)));
    private int ReadRecord(DemoApplication app, string kind, string id, int count)
    {
        var state = Unprotect<RecordState>(app, kind, "record", id);
        if (state.Index < 0 || state.Index >= count || state.ExpiresUtc <= _clock.GetUtcNow()) throw new ManagementDiagnosticNotFoundException();
        return state.Index;
    }
    private IDataProtector Protector(DemoApplication app, string kind, string context)
        => _protection.CreateProtector("SyntheticDemo", app.ApplicationKey, kind, _anchor.ToString("O"), context);
    private string Protect<T>(DemoApplication app, string kind, string context, T value)
        => "d1_" + Protector(app, kind, context).Protect(JsonSerializer.Serialize(value));
    private T Unprotect<T>(DemoApplication app, string kind, string context, string token)
    {
        try
        {
            if (!token.StartsWith("d1_", StringComparison.Ordinal) || token.Length > 8192) throw new DiagnosticTokenException();
            return JsonSerializer.Deserialize<T>(Protector(app, kind, context).Unprotect(token[3..])) ?? throw new DiagnosticTokenException();
        }
        catch { throw new DiagnosticTokenException(); }
    }
}
