using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace IBeam.Identity.Services.Tenants;

/// <summary>Optional external administration adapter. Never creates/selects a customer membership for its caller.</summary>
public sealed partial class IdentityTenantAdministrationService(IIdentityTenantService tenants, IIdentityTenantStore store,
    ITenantExtensionCoordinator extensions, IAuthLifecycleHook lifecycle, IAuthEventPublisher publisher,
    IOptions<AuthEventOptions> eventOptions, ILogger<IdentityTenantAdministrationService> logger, IServiceOperationExecutor operations)
    : IIdentityTenantAdministrationService
{
    public bool SupportsConditionalUpdates => store is IIdentityTenantConditionalStore;
    public Task<VersionedIdentityTenant?> FindAsync(Guid id, CancellationToken ct = default)
        => Run("read", id, async token => store is IIdentityTenantConditionalStore conditional ? await conditional.FindVersionedAsync(id, token) :
            await tenants.FindByIdAsync(id, token) is { } tenant ? new VersionedIdentityTenant(tenant, null, null) : null, ct);
    public Task CreateAsync(string name, Guid id, TenantExtensionContext context, CancellationToken ct = default)
        => Run("create", id, async token => { await tenants.CreateAsync(name, id, Safe(context), token); return true; }, ct);
    public Task ResumeCreationAsync(Guid id, TenantExtensionContext context, CancellationToken ct = default)
        => Run("resume-create", id, async token => {
            var tenant = await tenants.FindByIdAsync(id, token) ?? throw new IdentityNotFoundException("Tenant was not found.");
            await Created(tenant, Safe(context), token); return true;
        }, ct);
    public Task RenameAsync(Guid id, string name, string version, Guid operationId, TenantExtensionContext context, CancellationToken ct = default)
        => Run("rename", id, async token => {
            if (store is not IIdentityTenantConditionalStore conditional) throw new IdentityValidationException("Conditional tenant changes are unavailable.");
            var previous = await conditional.FindVersionedAsync(id, token) ?? throw new IdentityNotFoundException("Tenant was not found.");
            if (previous.Version != version) throw new IdentityConcurrencyException();
            var updated = await conditional.RenameAsync(id, name, version, operationId, token);
            await Updated(updated.Tenant, previous.Tenant, Safe(context) with { CausationId = operationId.ToString("D") }, token); return true;
        }, ct);
    public Task ResumeRenameAsync(Guid id, TenantExtensionContext context, CancellationToken ct = default)
        => Run("resume-rename", id, async token => {
            var tenant = await tenants.FindByIdAsync(id, token) ?? throw new IdentityNotFoundException("Tenant was not found.");
            await Updated(tenant, null, Safe(context), token); return true;
        }, ct);
    private static TenantExtensionContext Safe(TenantExtensionContext context) => context with { AuthUserId = null, RedactFailureDetails = true, RequireLifecycleDelivery = true };
    private async Task Created(IdentityTenant tenant, TenantExtensionContext context, CancellationToken ct)
    {
        await extensions.OnTenantCreatedAsync(tenant, context, ct);
        var evt = new TenantCreatedEvent { TenantId = tenant.TenantId, TenantName = tenant.Name, Status = tenant.Status,
            CorrelationId = context.CorrelationId, CausationId = context.CausationId, TraceId = context.TraceId };
        evt.Metadata["idempotencyKey"] = $"{TenantCreatedEvent.TypeName}:{tenant.TenantId:D}";
        await Publish(evt, token => lifecycle.OnTenantCreatedAsync(evt, token), context.RequireLifecycleDelivery, ct);
    }
    private async Task Updated(IdentityTenant tenant, IdentityTenant? previous, TenantExtensionContext context, CancellationToken ct)
    {
        await extensions.OnTenantUpdatedAsync(tenant, previous, context, ct);
        var evt = new TenantUpdatedEvent { TenantId = tenant.TenantId, TenantName = tenant.Name, PreviousTenantName = previous?.Name, Status = tenant.Status,
            CorrelationId = context.CorrelationId, CausationId = context.CausationId, TraceId = context.TraceId };
        evt.Metadata["idempotencyKey"] = $"{TenantUpdatedEvent.TypeName}:{tenant.TenantId:D}:{context.CausationId}";
        await Publish(evt, token => lifecycle.OnTenantUpdatedAsync(evt, token), context.RequireLifecycleDelivery, ct);
    }
    private async Task Publish<T>(T evt, Func<CancellationToken, Task> hook, bool requireDelivery, CancellationToken ct) where T : AuthLifecycleEventBase
    {
        try { await hook(ct); await publisher.PublishAsync(evt, ct); }
        catch (OperationCanceledException) { throw new OperationCanceledException("External tenant lifecycle delivery was cancelled.", ct); }
        catch { logger.LogError("External tenant lifecycle delivery failed."); if (eventOptions.Value.StrictPublishFailures || requireDelivery) throw new IdentityProviderException("Tenant lifecycle delivery is incomplete."); }
    }
    private Task<T> Run<T>(string action, Guid id, Func<CancellationToken, Task<T>> run, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            try { return await run(token); }
            catch (OperationCanceledException) { throw new OperationCanceledException("External tenant administration was cancelled.", token); }
            catch (IdentityConcurrencyException) { throw; }
            catch (IdentityNotFoundException) { throw new IdentityNotFoundException("Tenant was not found."); }
            catch { throw new IdentityProviderException("External tenant administration is incomplete."); }
        }, new ServiceOperationExecutionOptions { OperationName = "identity.tenants.external." + action, AuditAction = "identity.tenants.external." + action,
            EntityId = id, EntityName = "IdentityTenant", PermissionEnabled = false, AuditEnabled = true }, ct);
}
public static class IdentityTenantAdministrationExtensions
{
    public static IServiceCollection AddIBeamIdentityTenantAdministration(this IServiceCollection services)
    {
        services.TryAddScoped<IIdentityTenantAdministrationService, IdentityTenantAdministrationService>(); return services;
    }
}
