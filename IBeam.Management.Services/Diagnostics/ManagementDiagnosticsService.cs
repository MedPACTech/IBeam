using IBeam.Management.Models;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.Diagnostics;

public sealed class ManagementDiagnosticsService(IManagementApplicationAccessService access,
    IManagementDiagnosticConnectionResolver connections, IApplicationDiagnosticsClient client,
    IManagementApplicationStateProvider applications, PortalDiagnosticTokens tokens,
    IOptions<PortalDiagnosticsOptions> options, TimeProvider clock, IServiceOperationExecutor operations) : IManagementDiagnosticsService
{
    public Task<SystemErrorPage> ErrorsAsync(Guid applicationId, DiagnosticQuery query, CancellationToken ct = default)
        => Run("management.portal.errors.list", applicationId, async token =>
        {
            var c = await connections.ResolveAsync(applicationId, ManagementOperations.ErrorsRead, token);
            var state = Resolve(c, ManagementOperations.ErrorsRead, new() { FromUtc = query.FromUtc, ToUtc = query.ToUtc, PageSize = query.PageSize, Cursor = query.Cursor });
            c = await VerifyAsync(c, ManagementOperations.ErrorsRead, token);
            state = RequireMode(state, c.AllowUnsanitized);
            var page = await client.ErrorsAsync(c, new() { FromUtc = state.Query.FromUtc, ToUtc = state.Query.ToUtc,
                PageSize = state.Query.PageSize, Cursor = query.Cursor is null ? null : state.TargetToken }, token);
            await RecheckAsync(c, ManagementOperations.ErrorsRead, token);
            if (page.PageSize != state.Query.PageSize || page.Items.Count > state.Query.PageSize) throw new ManagementPortalResponseException();
            var items = page.Items.Select(x => Wrap(c, state, ApplicationDiagnosticsClient.SafeError(x, c.AllowUnsanitized))).ToArray();
            return new SystemErrorPage(items, page.PageSize, page.ContinuationToken is null ? null :
                tokens.Protect(c, ManagementOperations.ErrorsRead, "cursor", state with { TargetToken = page.ContinuationToken }));
        }, ct);

    public Task<SystemErrorDto> ErrorAsync(Guid applicationId, string id, CancellationToken ct = default)
        => Run("management.portal.errors.read", applicationId, async token =>
        {
            var c = await connections.ResolveAsync(applicationId, ManagementOperations.ErrorsRead, token);
            var state = tokens.Unprotect(c, ManagementOperations.ErrorsRead, "record", id, clock.GetUtcNow());
            c = await VerifyAsync(c, ManagementOperations.ErrorsRead, token);
            state = RequireMode(state, c.AllowUnsanitized);
            var row = await client.ErrorAsync(c, state.TargetToken, token);
            await RecheckAsync(c, ManagementOperations.ErrorsRead, token);
            if (row.Id != state.TargetToken) throw new ManagementPortalResponseException();
            return Wrap(c, state, ApplicationDiagnosticsClient.SafeError(row, c.AllowUnsanitized)) with { Id = id };
        }, ct);

    public Task<SystemLogPage> LogsAsync(Guid applicationId, SystemLogQuery query, CancellationToken ct = default)
        => Run("management.portal.logs.list", applicationId, async token =>
        {
            var c = await connections.ResolveAsync(applicationId, ManagementOperations.LogsRead, token);
            var state = Resolve(c, ManagementOperations.LogsRead, query);
            c = await VerifyAsync(c, ManagementOperations.LogsRead, token);
            state = RequireMode(state, c.AllowUnsanitized);
            var page = await client.LogsAsync(c, state.Query with { Cursor = query.Cursor is null ? null : state.TargetToken }, token);
            await RecheckAsync(c, ManagementOperations.LogsRead, token);
            if (page.PageSize != state.Query.PageSize || page.Items.Count > state.Query.PageSize) throw new ManagementPortalResponseException();
            var items = page.Items.Select(x => Wrap(c, state, ApplicationDiagnosticsClient.SafeLog(x, c.AllowUnsanitized))).ToArray();
            return new SystemLogPage(items, page.PageSize, page.ContinuationToken is null ? null :
                tokens.Protect(c, ManagementOperations.LogsRead, "cursor", state with { TargetToken = page.ContinuationToken }));
        }, ct);

    public Task<SystemLogDto> LogAsync(Guid applicationId, string id, CancellationToken ct = default)
        => Run("management.portal.logs.read", applicationId, async token =>
        {
            var c = await connections.ResolveAsync(applicationId, ManagementOperations.LogsRead, token);
            var state = tokens.Unprotect(c, ManagementOperations.LogsRead, "record", id, clock.GetUtcNow());
            c = await VerifyAsync(c, ManagementOperations.LogsRead, token);
            state = RequireMode(state, c.AllowUnsanitized);
            var row = await client.LogAsync(c, state.TargetToken, token);
            await RecheckAsync(c, ManagementOperations.LogsRead, token);
            if (row.Id != state.TargetToken) throw new ManagementPortalResponseException();
            return Wrap(c, state, ApplicationDiagnosticsClient.SafeLog(row, c.AllowUnsanitized)) with { Id = id };
        }, ct);

    private PortalDiagnosticState Resolve(ManagementDiagnosticConnection c, string permission, SystemLogQuery query)
    {
        var now = clock.GetUtcNow();
        var previous = query.Cursor is null ? null : tokens.Unprotect(c, permission, "cursor", query.Cursor, now);
        var saved = previous?.Query;
        var from = query.FromUtc ?? saved?.FromUtc ?? now.AddHours(-24);
        var to = query.ToUtc ?? saved?.ToUtc ?? now;
        var size = query.PageSize ?? saved?.PageSize ?? 50;
        var source = query.Source ?? saved?.Source; var level = query.Level ?? saved?.Level; var trace = query.TraceId ?? saved?.TraceId;
        if (from.Offset != TimeSpan.Zero || to.Offset != TimeSpan.Zero || from >= to || to - from > TimeSpan.FromHours(168) ||
            size is < 1 or > 100 || source?.Length > 128 || trace?.Length > 128 ||
            source?.Any(char.IsControl) == true || trace?.Any(char.IsControl) == true || level is not null && !SystemLogLevels.All.Contains(level))
            throw new DiagnosticQueryException();
        var frozen = new SystemLogQuery { FromUtc = from, ToUtc = to, PageSize = size, Source = source, Level = level, TraceId = trace };
        if (saved is not null && frozen != saved) throw new DiagnosticQueryException();
        return previous ?? new PortalDiagnosticState("d1_initial", frozen, now.AddMinutes(options.Value.TokenLifetimeMinutes));
    }
    private async Task<ManagementDiagnosticConnection> VerifyAsync(ManagementDiagnosticConnection c, string permission, CancellationToken ct)
    {
        var info = await client.InfoAsync(c, ct);
        try { ManagementConnectionProtocol.Validate(info); } catch { throw new ManagementPortalResponseException(); }
        if (info.ApplicationKey != c.ApplicationKey || info.Environment != c.Environment) throw new ManagementPortalResponseException();
        if (!info.Capabilities.Contains(permission, StringComparer.Ordinal)) throw new ManagementAccessDeniedException();
        await RecheckAsync(c, permission, ct);
        return new ManagementDiagnosticConnection { OrganizationId = c.OrganizationId, UserId = c.UserId, ApplicationId = c.ApplicationId,
            ApplicationKey = c.ApplicationKey, Environment = c.Environment, BaseUrl = c.BaseUrl, CredentialId = c.CredentialId,
            Version = c.Version, Key = c.Key, Capabilities = c.Capabilities,
            AllowUnsanitized = info.UnsanitizedCapabilities?.Contains(permission, StringComparer.Ordinal) == true };
    }
    private static PortalDiagnosticState RequireMode(PortalDiagnosticState state, bool raw)
    {
        if (state.Unsanitized.HasValue && state.Unsanitized.Value != raw) throw new DiagnosticTokenException();
        return state with { Unsanitized = raw };
    }
    private async Task RecheckAsync(ManagementDiagnosticConnection c, string permission, CancellationToken ct)
    {
        await access.RequireAsync(c.ApplicationId, permission, ct);
        var app = await applications.GetAsync(c.OrganizationId, c.ApplicationId, ct);
        if (app is null || !app.IsEnabled || app.Version != c.Version || app.ActiveCredentialId != c.CredentialId || app.BaseUrl != c.BaseUrl)
            throw new ManagementAccessDeniedException();
    }
    private SystemErrorDto Wrap(ManagementDiagnosticConnection c, PortalDiagnosticState state, SystemErrorDto row)
    {
        Within(row.OccurredAtUtc, state.Query);
        return row with { Id = tokens.Protect(c, ManagementOperations.ErrorsRead, "record", state with { TargetToken = row.Id }) };
    }
    private SystemLogDto Wrap(ManagementDiagnosticConnection c, PortalDiagnosticState state, SystemLogDto row)
    {
        Within(row.OccurredAtUtc, state.Query);
        return row with { Id = tokens.Protect(c, ManagementOperations.LogsRead, "record", state with { TargetToken = row.Id }) };
    }
    private static void Within(DateTimeOffset occurred, SystemLogQuery query)
    {
        if (occurred < query.FromUtc || occurred >= query.ToUtc) throw new ManagementPortalResponseException();
    }
    private Task<T> Run<T>(string operation, Guid app, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => operations.ExecuteAsync(this, async token =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try { return await action(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException("Portal diagnostic request was cancelled.", ct);
                throw new ManagementConnectionTimeoutException();
            }
            catch (System.Text.Json.JsonException) { throw new ManagementPortalResponseException(); }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or DiagnosticQueryException or DiagnosticTokenException or
                ManagementDiagnosticNotFoundException or ManagementDiagnosticUnavailableException or ManagementPortalResponseException or ManagementPortalRateLimitException))
            { throw new ManagementDiagnosticUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = operation, EntityId = app,
            EntityName = "ManagementDiagnostics", PermissionEnabled = false, AuditEnabled = true }, ct);
}
