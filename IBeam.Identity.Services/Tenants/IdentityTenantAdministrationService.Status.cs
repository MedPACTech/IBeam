using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
namespace IBeam.Identity.Services.Tenants;
public sealed partial class IdentityTenantAdministrationService : IIdentityTenantStatusAdministrationService
{
    public bool SupportsConditionalStatus => store is IIdentityTenantStatusConditionalStore && store is IIdentityTenantConditionalStore;
    public Task SetStatusAsync(Guid tenant, bool active, string version, Guid operation, TenantExtensionContext context, CancellationToken ct = default)
        => Run("status", tenant, async token => {
            if (store is not IIdentityTenantStatusConditionalStore conditional) throw new IdentityValidationException("Conditional tenant status is unavailable.");
            var updated = await conditional.SetStatusConditionalAsync(tenant, active, version, operation, token);
            await StatusHooks(updated.Tenant, active, Safe(context) with { CausationId = operation.ToString("D") }, token); return true;
        }, ct);
    public Task ResumeStatusAsync(Guid tenant, bool active, TenantExtensionContext context, CancellationToken ct = default)
        => Run("resume-status", tenant, async token => {
            if (store is not IIdentityTenantConditionalStore conditional || !Guid.TryParseExact(context.CausationId, "D", out var operation)) throw new IdentityConcurrencyException();
            var current = await conditional.FindVersionedAsync(tenant, token) ?? throw new IdentityNotFoundException("Tenant was not found.");
            if (current.Tenant.IsActive != active || current.LastOperationId != operation) throw new IdentityConcurrencyException();
            await StatusHooks(current.Tenant, active, Safe(context), token); return true;
        }, ct);
    private async Task StatusHooks(IdentityTenant tenant, bool active, TenantExtensionContext context, CancellationToken ct)
    {
        try {
            if (active) {
                await extensions.OnTenantActivatedAsync(tenant, context, ct);
                var evt = new TenantActivatedEvent { TenantId = tenant.TenantId, TenantName = tenant.Name, CorrelationId = context.CorrelationId,
                    CausationId = context.CausationId, TraceId = context.TraceId };
                evt.Metadata["idempotencyKey"] = $"{TenantActivatedEvent.TypeName}:{tenant.TenantId:D}:{context.CausationId}";
                await Publish(evt, token => lifecycle.OnTenantActivatedAsync(evt, token), true, ct);
            } else {
                await extensions.OnTenantDeactivatedAsync(tenant, context, ct);
                var evt = new TenantDeactivatedEvent { TenantId = tenant.TenantId, TenantName = tenant.Name, CorrelationId = context.CorrelationId,
                    CausationId = context.CausationId, TraceId = context.TraceId };
                evt.Metadata["idempotencyKey"] = $"{TenantDeactivatedEvent.TypeName}:{tenant.TenantId:D}:{context.CausationId}";
                await Publish(evt, token => lifecycle.OnTenantDeactivatedAsync(evt, token), true, ct);
            }
        } catch (OperationCanceledException) { throw; }
        catch { throw new IdentityProviderException("Tenant lifecycle delivery is incomplete."); }
    }
}
