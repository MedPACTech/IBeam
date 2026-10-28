using System.Diagnostics;
using System.Diagnostics.Metrics;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Operations;
using IBeam.Management.Models;

namespace IBeam.Management.Services;

/// <summary>Subscribe through DiagnosticListener; completion events contain only the bounded public metadata record.
/// No ambient Activity baggage or generic objects are copied. Observers must be nonblocking.</summary>
public static class ManagementTargetTelemetry
{
    public const string SourceName = "IBeam.Management.TargetOperations";
    public const string CompletionEvent = "target.operation.completed";
    private static readonly DiagnosticListener Source = new(SourceName);
    private static readonly Meter Meter = new(SourceName);
    private static readonly Counter<long> Calls = Meter.CreateCounter<long>("management.target.calls");
    private static readonly Histogram<long> Duration = Meter.CreateHistogram<long>("management.target.duration", "ms");
    private static readonly AsyncLocal<Attempt?> Current = new();

    internal static async Task<T> Run<T>(string channel, string kind, string operation, Guid? application, Guid? tenant, Guid? resource,
        IManagementAuditContext? audit, CancellationToken callerToken, Func<Task<T>> body)
    {
        var parent = Current.Value; var attempt = new Attempt { Correlation = Correlation(audit, parent), CallerToken = callerToken };
        var started = Stopwatch.GetTimestamp(); Current.Value = attempt; Exception? failure = null;
        try { return await body(); }
        catch (Exception ex) { failure = ex; throw; }
        finally {
            Current.Value = parent;
            try {
                var (outcome, code) = Classify(failure, attempt);
                var elapsed = (long)Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 0, 86_400_000);
                var record = new ManagementTargetCallTelemetry(DateTimeOffset.UtcNow, elapsed, channel, kind, operation,
                    Id(application), Id(tenant), Id(resource), Id(attempt.OperationId), attempt.Correlation, outcome, code, attempt.Status, attempt.WriteSent);
                // Each emitter is isolated: a listener or meter callback cannot change a result, authority or retry count.
                try { if (Source.IsEnabled(CompletionEvent)) Source.Write(CompletionEvent, record); } catch { }
                var tags = new TagList { { "operation", operation }, { "channel", channel }, { "outcome", outcome } };
                try { Calls.Add(1, tags); } catch { }
                try { Duration.Record(elapsed, tags); } catch { }
            } catch { }
        }
    }
    internal static void Operation(Guid id) { if (Current.Value is { } state && id != Guid.Empty) state.OperationId = id; }
    internal static void Unknown() { if (Current.Value is { } state) state.Unknown = true; }
    internal static string Registered(ManagementOperationRegistry registry, string? operation) => operation is not null ? registry.Find(operation)?.Operation ?? "unregistered" : "unregistered";
    internal static string Builtin(string operation) => operation is not null && ManagementOperationRegistry.BuiltIn.Contains(operation) ? operation : "unregistered";
    internal static Guid? Tenant(string path)
    {
        var parts = path.Split('?', 2)[0].Split('/');
        for (var i = 0; i + 1 < parts.Length; i++) if (parts[i] == "tenants" && Guid.TryParseExact(parts[i + 1], "D", out var id)) return Id(id);
        return null;
    }
    internal sealed class Transport : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var state = Current.Value;
            if (state is not null && request.Method != HttpMethod.Get) state.WriteSent = true;
            try {
                var response = await base.SendAsync(request, ct);
                if (state is not null) state.Status = (int)response.StatusCode;
                return response;
            } catch (OperationCanceledException) { if (state is not null) state.Failure = state.CallerToken.IsCancellationRequested ? "cancelled" : "timeout"; throw; }
            catch (HttpRequestException) { if (state is not null) state.Failure = "network"; throw; }
        }
    }
    private static (string Outcome, string Code) Classify(Exception? error, Attempt state)
    {
        if (error is ManagementOperationUncertainException unknown) { state.OperationId = unknown.OperationId; return ("unknown", state.Failure ?? "response"); }
        if (error is null) return state.Unknown ? ("unknown", "unknown") : ("success", "none");
        if (error is OperationCanceledException) return state.CallerToken.IsCancellationRequested ? ("cancelled", "cancelled") : ("timeout", "timeout");
        if (error is ManagementConnectionTimeoutException) return ("timeout", "timeout");
        if (error is ManagementAccessDeniedException) return ("rejected", "authorization");
        if (error is ManagementOperationInvalidException or ManagementCustomerIdentityInvalidRequestException or DiagnosticQueryException or ManagementConnectionInvalidException or ManagementApplicationInvalidException) return ("rejected", "invalid");
        if (error is ManagementOperationConflictException or ManagementIdentityOperationConflictException) return ("rejected", "conflict");
        if (error is ManagementOperationPreconditionException or ManagementCustomerIdentityPreconditionException) return ("rejected", "precondition");
        if (error is ManagementCustomerIdentityRateLimitException or ManagementPortalRateLimitException) return ("rejected", "rate-limit");
        if (error is ManagementCustomerIdentityNotFoundException or ManagementDiagnosticNotFoundException) return ("rejected", "not-found");
        return state.Failure switch { "timeout" => ("timeout", "timeout"), "cancelled" => ("cancelled", "cancelled"), "network" => ("network-failure", "network"), _ => ("unavailable", "response") };
    }
    private static string Correlation(IManagementAuditContext? audit, Attempt? parent)
    {
        try { var value = audit?.CorrelationId ?? parent?.Correlation; if (value is { Length: 32 } && value.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f')) return value; } catch { }
        return Guid.NewGuid().ToString("N");
    }
    private static Guid? Id(Guid? value) => value is { } id && id != Guid.Empty ? id : null;
    private sealed class Attempt { public string Correlation = ""; public CancellationToken CallerToken; public string? Failure; public int? Status; public Guid? OperationId; public bool Unknown, WriteSent; }
}
