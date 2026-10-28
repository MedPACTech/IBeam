using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Identity.Target.Services.Invitations;

public sealed class ManagementBulkInvitationRevokeOptions
{
    public bool Enabled { get; set; }
    public int MaximumBatchSize { get; set; } = 50;
    public int MaximumItemsPerAttempt { get; set; } = 10;
    public int AttemptSeconds { get; set; } = 5;
    public int RetentionDays { get; set; } = 7;
    public bool Valid() => MaximumBatchSize is >= 1 and <= 50 && MaximumItemsPerAttempt is >= 1 and <= 10 && MaximumItemsPerAttempt <= MaximumBatchSize &&
        AttemptSeconds is >= 1 and <= 20 && RetentionDays is >= 1 and <= 30;
}

public sealed class ManagementBulkInvitationRevokeService(IManagementBulkInvitationBatchRepository journal, ITenantInviteStore invites,
    IManagedTenantInviteStore transitions, IIdentityTenantStore tenants, IManagementOperationAccessService access,
    IOptions<ManagementBulkInvitationRevokeOptions> options, IOptions<ManagementCustomerIdentityTargetOptions> native,
    IOptions<ManagementDiagnosticsOptions> deployment, TimeProvider clock, IServiceOperationExecutor operations) : IManagementBulkInvitationRevokeService
{
    public Task<ManagementBulkInvitationRevokeResult> ExecuteAsync(ManagementOperationContext scope, ManagementBulkInvitationRevokeRequest request, CancellationToken ct = default)
        => Run(scope, async root => {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(root); deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.AttemptSeconds, 1, 20))); var token = deadline.Token;
            await Require(scope, token); ManagementBulkInvitationRevokeOperation.Validate(request);
            if (request.Invitations.Length > options.Value.MaximumBatchSize || !ManagementBulkInvitationRevokeOperation.Exact(scope.ResourceVersion)) throw new ManagementOperationInvalidException();
            // Freeze caller-owned selection before persistence/mutation awaits.
            var selected = request.Invitations.OrderBy(x => x.InviteId).Select(x => x with { }).ToArray();
            var fingerprint = Fingerprint(scope.ResourceVersion, selected);
            var row = await journal.GetAsync(scope, token).WaitAsync(token);
            if (row is null) {
                var tenant = await RequireTenant(scope, token);
                if (tenant != scope.ResourceVersion) throw new ManagementOperationPreconditionException();
                var now = clock.GetUtcNow();
                row = new(scope with { }, deployment.Value.ApplicationKey, deployment.Value.Environment, now, now.AddDays(options.Value.RetentionDays), fingerprint,
                    selected.Select(x => new ManagementBulkInvitationItem(x.InviteId, ItemId(scope.OperationId, x.InviteId), x.ExpectedVersion, "pending", null)).ToArray());
                _ = await journal.TryCreateAsync(row, token).WaitAsync(token);
                row = await journal.GetAsync(scope, token).WaitAsync(token) ?? throw new ManagementOperationUnavailableException();
            }
            Validate(row, scope); if (row.Fingerprint != fingerprint) throw new ManagementOperationConflictException();
            var work = row.Items.Where(x => !ManagementBulkInvitationRevokeOperation.Terminal(x.State)).OrderBy(x => x.State == "pending" ? 0 : 1).ThenBy(x => x.InviteId)
                .Take(options.Value.MaximumItemsPerAttempt).Select(x => x.InviteId).ToArray();
            foreach (var id in work) {
                if (token.IsCancellationRequested) { root.ThrowIfCancellationRequested(); break; }
                var item = row.Items.Single(x => x.InviteId == id);
                try {
                    await Require(scope, token); _ = await RequireTenant(scope, token);
                    var current = await ReadInvite(scope.TenantId, id, token);
                    var outcome = Classify(current, item, clock.GetUtcNow());
                    if (outcome is { } known) { row = await Save(row, item with { State = known.State, FailureCode = known.Code }, token); continue; }
                    row = await Save(row, item with { State = "started", FailureCode = null }, token);
                    await Require(scope, token);
                    // Native CAS and its verified acceptance claim remain authoritative. No mail, roles or global user service is involved.
                    var changed = await transitions.TransitionAsync(new() { TenantId = scope.TenantId, InviteId = id, OperationId = item.OperationId,
                        Kind = "revoke", ExpectedVersion = item.ExpectedVersion, NowUtc = row.CreatedUtc,
                        ExternalActor = new(scope.OrganizationId, scope.ActorUserId, scope.ApplicationId, item.OperationId) }, token).WaitAsync(token);
                    if (changed.TenantId != scope.TenantId || changed.InviteId != id || changed.LastTransitionOperationId != item.OperationId || changed.Status != TenantInviteStatuses.Revoked)
                        throw new ManagementOperationUnavailableException();
                    row = await Save(row, item with { State = "revoked", FailureCode = null }, token);
                }
                catch (ManagementAccessDeniedException) { throw; }
                catch (ManagementOperationConflictException) { row = await Reload(row, scope, fingerprint, token); break; }
                catch (OperationCanceledException) {
                    row = await SaveAfterFailure(row, item, root.IsCancellationRequested ? "cancelled" : "deadline");
                    root.ThrowIfCancellationRequested(); break;
                }
                catch (IdentityConcurrencyException) {
                    var current = await ReadInvite(scope.TenantId, id, token);
                    var outcome = Classify(current, item, clock.GetUtcNow());
                    row = await Save(row, item with { State = outcome?.State ?? "unknown", FailureCode = outcome?.Code ?? "provider" }, token);
                }
                catch (ManagementCustomerIdentityRateLimitException) { row = await Save(row, item with { State = "failed", FailureCode = "rate-limited" }, token); break; }
                catch { row = await SaveAfterFailure(row, item, "provider"); break; }
            }
            using var after = CancellationTokenSource.CreateLinkedTokenSource(root); after.CancelAfter(TimeSpan.FromSeconds(2));
            await Require(scope, after.Token); return Result(row, "accepted");
        }, ct);

    public Task<ManagementBulkInvitationRevokeResult> ReadReceiptAsync(ManagementOperationContext scope, CancellationToken ct = default)
        => Run(scope, async root => {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(root); deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Value.AttemptSeconds, 1, 20))); var token = deadline.Token;
            await Require(scope, token); var row = await journal.GetAsync(scope, token).WaitAsync(token);
            if (row is null) return new(scope.TenantId, scope.ResourceId, scope.OperationId, "unknown", "unknown", 0, false, []);
            Validate(row, scope); await Require(scope, token); return Result(row, "found");
        }, ct);

    private async Task Require(ManagementOperationContext scope, CancellationToken ct)
    {
        if (!options.Value.Enabled || !options.Value.Valid() || journal is not { ProtocolVersion: 1, Durable: true } || access.ProtocolVersion != 1 ||
            scope is null || scope.OrganizationId == Guid.Empty || scope.ApplicationId == Guid.Empty || scope.RealmId == Guid.Empty || scope.TenantId == Guid.Empty ||
            scope.ActorUserId == Guid.Empty || scope.OperationId == Guid.Empty || scope.ResourceId == Guid.Empty || !native.Value.DedicatedApplicationRealm ||
            native.Value.IdentityRealmId != scope.RealmId || string.IsNullOrWhiteSpace(deployment.Value.ApplicationKey) || string.IsNullOrWhiteSpace(deployment.Value.Environment))
            throw new ManagementAccessDeniedException();
        await access.RequireAsync(ManagementBulkInvitationRevokeOperation.Name, scope, ct).WaitAsync(ct);
    }
    private async Task<string> RequireTenant(ManagementOperationContext scope, CancellationToken ct)
    {
        if (tenants is not IIdentityTenantConditionalStore conditional) throw new ManagementOperationUnavailableException();
        return (await conditional.FindVersionedAsync(scope.TenantId, ct).WaitAsync(ct))?.Version ?? throw new ManagementAccessDeniedException();
    }
    private void Validate(ManagementBulkInvitationBatch row, ManagementOperationContext scope)
    {
        if ((row.Scope with { ResourceVersion = null }) != (scope with { ResourceVersion = null }) || row.ApplicationKey != deployment.Value.ApplicationKey || row.Environment != deployment.Value.Environment)
            throw new ManagementOperationConflictException();
        if (row.ExpiresUtc <= clock.GetUtcNow()) throw new ManagementOperationPreconditionException();
        if (row.CreatedUtc > clock.GetUtcNow() || row.ExpiresUtc <= row.CreatedUtc || row.ExpiresUtc - row.CreatedUtc > TimeSpan.FromDays(30) ||
            !ManagementBulkInvitationRevokeOperation.Exact(row.Scope.ResourceVersion) || row.Fingerprint is not { Length: 64 } || !ManagementBulkInvitationRevokeOperation.Exact(row.Version) || row.Items is not { Length: > 0 and <= 50 } ||
            row.Items.Length > options.Value.MaximumBatchSize ||
            row.Items.Select(x => x.InviteId).Distinct().Count() != row.Items.Length || row.Items.Any(x => x.InviteId == Guid.Empty || x.OperationId != ItemId(scope.OperationId, x.InviteId) ||
                !ManagementBulkInvitationRevokeOperation.Exact(x.ExpectedVersion) || x.State is not ("pending" or "started" or "unknown" or "failed" or "revoked" or "already-revoked" or "not-eligible" or "changed-version")))
            throw new ManagementOperationUnavailableException();
        if (row.Fingerprint != Fingerprint(row.Scope.ResourceVersion, row.Items.OrderBy(x => x.InviteId).Select(x => new ManagementBulkInvitationSelection(x.InviteId, x.ExpectedVersion)).ToArray()))
            throw new ManagementOperationUnavailableException();
    }
    private async Task<TenantInviteRecord?> ReadInvite(Guid tenant, Guid id, CancellationToken ct)
    {
        var row = await invites.GetAsync(tenant, id, ct).WaitAsync(ct);
        if (row is not null && (row.TenantId != tenant || row.InviteId != id)) throw new ManagementOperationUnavailableException(); return row;
    }
    private static string Fingerprint(string? version, ManagementBulkInvitationSelection[] items) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { ResourceVersion = version, Items = items })));
    private async Task<ManagementBulkInvitationBatch> Save(ManagementBulkInvitationBatch row, ManagementBulkInvitationItem item, CancellationToken ct)
        => await journal.ReplaceAsync(row with { Items = row.Items.Select(x => x.InviteId == item.InviteId ? item : x).ToArray() }, row.Version, ct).WaitAsync(ct);
    private async Task<ManagementBulkInvitationBatch> SaveAfterFailure(ManagementBulkInvitationBatch row, ManagementBulkInvitationItem item, string code)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { return await Save(row, item with { State = "unknown", FailureCode = code }, cleanup.Token); }
        catch { return row; } // 'started' projects as unknown; an ambiguous checkpoint must never claim success.
    }
    private async Task<ManagementBulkInvitationBatch> Reload(ManagementBulkInvitationBatch row, ManagementOperationContext scope, string fingerprint, CancellationToken ct)
    {
        var current = await journal.GetAsync(scope, ct).WaitAsync(ct) ?? throw new ManagementOperationUnavailableException();
        Validate(current, scope); if (current.Fingerprint != fingerprint) throw new ManagementOperationConflictException(); return current;
    }
    private static (string State, string? Code)? Classify(TenantInviteRecord? current, ManagementBulkInvitationItem item, DateTimeOffset now)
    {
        if (current is null) return ("not-eligible", "missing");
        if (current.Status == TenantInviteStatuses.Revoked) return current.LastTransitionOperationId == item.OperationId ? null : ("already-revoked", null);
        if (current.AcceptanceUserId is not null || current.Status == TenantInviteStatuses.Redeemed) return ("not-eligible", "accepted");
        if (current.ExpiresUtc <= now || current.Status == TenantInviteStatuses.Expired) return ("not-eligible", "expired");
        if (current.Status is not (TenantInviteStatuses.Pending or TenantInviteStatuses.Sent)) throw new ManagementOperationUnavailableException();
        return current.Version != item.ExpectedVersion ? ("changed-version", "version-changed") : null;
    }
    private static Guid ItemId(Guid batch, Guid invite) => ManagementBulkInvitationRevokeOperation.ItemOperationId(batch, invite);
    private static ManagementBulkInvitationRevokeResult Result(ManagementBulkInvitationBatch row, string outcome)
    {
        var items = row.Items.Select(x => new ManagementBulkInvitationItemResult(x.InviteId, x.OperationId, x.State == "started" ? "unknown" : x.State, x.FailureCode)).ToArray();
        var state = ManagementBulkInvitationRevokeOperation.State(items);
        return ManagementBulkInvitationRevokeOperation.Project(new(row.Scope.TenantId, row.Scope.ResourceId, row.Scope.OperationId, outcome == "accepted" && state == "unknown" ? "unknown" : outcome,
            state, items.Length, items.All(x => ManagementBulkInvitationRevokeOperation.Terminal(x.State)), items), row.Scope.TenantId, row.Scope.ResourceId, row.Scope.OperationId);
    }
    private Task<ManagementBulkInvitationRevokeResult> Run(ManagementOperationContext scope, Func<CancellationToken, Task<ManagementBulkInvitationRevokeResult>> body, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            try { return await body(token); }
            catch (OperationCanceledException) { throw new OperationCanceledException("The bulk invitation attempt was cancelled.", ct); }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementOperationInvalidException or ManagementOperationPreconditionException or ManagementOperationConflictException) { throw; }
            catch { throw new ManagementOperationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = ManagementBulkInvitationRevokeOperation.Name, AuditAction = ManagementBulkInvitationRevokeOperation.Name,
            EntityName = "ManagementBulkInvitationBatch", EntityId = scope?.ResourceId, TenantId = scope?.TenantId, PermissionEnabled = false, AuditEnabled = true }, ct);
}

public sealed class ManagementBulkInvitationRevokeHandler(IManagementBulkInvitationRevokeService service) :
    IManagementOperationHandler<ManagementBulkInvitationRevokeRequest, ManagementBulkInvitationRevokeResult>, IManagementOperationReceiptHandler<ManagementBulkInvitationRevokeResult>
{
    public Task<ManagementBulkInvitationRevokeResult> ExecuteAsync(ManagementOperationContext scope, ManagementBulkInvitationRevokeRequest request, CancellationToken ct = default) => service.ExecuteAsync(scope, request, ct);
    public Task<ManagementBulkInvitationRevokeResult> ReadReceiptAsync(ManagementOperationContext scope, CancellationToken ct = default) => service.ReadReceiptAsync(scope, ct);
}
public static class ManagementBulkInvitationRevokeExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementBulkInvitationRevokeTarget(this IServiceCollection services, IConfiguration configuration, Action<ManagementBulkInvitationRevokeOptions> configure)
    {
        var settings = new ManagementBulkInvitationRevokeOptions(); configure(settings); if (!settings.Enabled || services.Any(x => x.ServiceType == typeof(Registration))) return services;
        if (!settings.Valid()) throw new ArgumentException("A bounded invitation batch/attempt and retention policy is required.", nameof(configure));
        services.AddSingleton<Registration>();
        services.AddIBeamManagementCustomerIdentityTargetServices(configuration); services.AddSingleton<IOptions<ManagementBulkInvitationRevokeOptions>>(Options.Create(settings));
        services.TryAddScoped<IManagedTenantInviteStore>(p => p.GetRequiredService<ITenantInviteStore>() as IManagedTenantInviteStore ??
            throw new InvalidOperationException("Native conditional invitation transitions are required."));
        services.TryAddSingleton(TimeProvider.System); services.TryAddScoped<IManagementBulkInvitationRevokeService, ManagementBulkInvitationRevokeService>();
        services.AddIBeamManagementOperation<ManagementBulkInvitationRevokeRequest, ManagementBulkInvitationRevokeResult, ManagementBulkInvitationRevokeHandler>(ManagementBulkInvitationRevokeOperation.Descriptor);
        services.AddHostedService<Validate>(); return services;
    }
    private sealed class Validate(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); var p = scope.ServiceProvider;
            if (p.GetRequiredService<IManagementBulkInvitationBatchRepository>() is not { ProtocolVersion: 1, Durable: true } ||
                p.GetRequiredService<IIdentityTenantStore>() is not IIdentityTenantConditionalStore || p.GetRequiredService<IManagementOperationAccessService>().ProtocolVersion != 1 ||
                p.GetRequiredService<IOptions<ManagementCustomerIdentityTargetOptions>>().Value.IdentityRealmId != p.GetRequiredService<IOptions<ManagementOperationTargetOptions>>().Value.RealmId)
                throw new InvalidOperationException("Bulk invitation revocation requires durable journal storage, native conditional providers and the same dedicated realm.");
            _ = p.GetRequiredService<IManagedTenantInviteStore>(); _ = p.GetRequiredService<IManagementBulkInvitationRevokeService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
