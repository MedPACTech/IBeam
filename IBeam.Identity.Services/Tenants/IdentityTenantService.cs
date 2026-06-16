using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Tenants;

public sealed class IdentityTenantService : IIdentityTenantService
{
    private readonly IIdentityTenantStore _tenants;
    private readonly ITenantExtensionCoordinator _tenantExtensions;
    private readonly IAuthEventPublisher _eventPublisher;
    private readonly IAuthLifecycleHook _lifecycleHook;
    private readonly IOptions<AuthEventOptions> _eventOptions;
    private readonly ILogger<IdentityTenantService> _logger;

    public IdentityTenantService(
        IIdentityTenantStore tenants,
        ITenantExtensionCoordinator tenantExtensions,
        IAuthEventPublisher eventPublisher,
        IAuthLifecycleHook lifecycleHook,
        IOptions<AuthEventOptions> eventOptions,
        ILogger<IdentityTenantService> logger)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tenantExtensions = tenantExtensions ?? throw new ArgumentNullException(nameof(tenantExtensions));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _lifecycleHook = lifecycleHook ?? throw new ArgumentNullException(nameof(lifecycleHook));
        _eventOptions = eventOptions ?? throw new ArgumentNullException(nameof(eventOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IdentityTenant?> FindByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        return _tenants.FindByIdAsync(tenantId, ct);
    }

    public async Task<IdentityTenant> CreateAsync(
        string name,
        Guid? tenantId = null,
        TenantExtensionContext? context = null,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var tenant = new IdentityTenant(
            tenantId.GetValueOrDefault(Guid.NewGuid()),
            NormalizeTenantName(name),
            IdentityTenant.NormalizeName(name),
            IdentityTenantStatuses.Active,
            now);

        var created = await _tenants.CreateAsync(tenant, ct).ConfigureAwait(false);
        var lifecycleContext = context ?? TenantExtensionContext.Create(TenantExtensionOperations.Created);

        await _tenantExtensions.OnTenantCreatedAsync(created, lifecycleContext, ct).ConfigureAwait(false);

        var evt = new TenantCreatedEvent
        {
            TenantId = created.TenantId,
            TenantName = created.Name,
            Status = created.Status,
            CorrelationId = lifecycleContext.CorrelationId,
            CausationId = lifecycleContext.CausationId,
            TraceId = lifecycleContext.TraceId
        };
        evt.Metadata["idempotencyKey"] = $"{TenantCreatedEvent.TypeName}:{created.TenantId:D}";
        await InvokeLifecycleAndPublishAsync(evt, (hook, e, token) => hook.OnTenantCreatedAsync(e, token), ct)
            .ConfigureAwait(false);

        return created;
    }

    public async Task<IdentityTenant> UpdateAsync(
        IdentityTenant tenant,
        TenantExtensionContext? context = null,
        CancellationToken ct = default)
    {
        ValidateIdentityTenant(tenant);

        var previous = await _tenants.FindByIdAsync(tenant.TenantId, ct).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"Tenant '{tenant.TenantId}' was not found.");

        var normalized = tenant with
        {
            Name = NormalizeTenantName(tenant.Name),
            NormalizedName = string.IsNullOrWhiteSpace(tenant.NormalizedName)
                ? IdentityTenant.NormalizeName(tenant.Name)
                : tenant.NormalizedName.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var updated = await _tenants.UpdateAsync(normalized, ct).ConfigureAwait(false);
        var lifecycleContext = context ?? TenantExtensionContext.Create(TenantExtensionOperations.Updated);

        await _tenantExtensions.OnTenantUpdatedAsync(updated, previous, lifecycleContext, ct).ConfigureAwait(false);

        var evt = new TenantUpdatedEvent
        {
            TenantId = updated.TenantId,
            TenantName = updated.Name,
            PreviousTenantName = previous.Name,
            Status = updated.Status,
            CorrelationId = lifecycleContext.CorrelationId,
            CausationId = lifecycleContext.CausationId,
            TraceId = lifecycleContext.TraceId
        };
        evt.Metadata["idempotencyKey"] = $"{TenantUpdatedEvent.TypeName}:{updated.TenantId:D}";
        await InvokeLifecycleAndPublishAsync(evt, (hook, e, token) => hook.OnTenantUpdatedAsync(e, token), ct)
            .ConfigureAwait(false);

        return updated;
    }

    public Task<IdentityTenant> ActivateAsync(Guid tenantId, TenantExtensionContext? context = null, CancellationToken ct = default)
        => SetStatusAsync(tenantId, IdentityTenantStatuses.Active, context ?? TenantExtensionContext.Create(TenantExtensionOperations.Activated), ct);

    public Task<IdentityTenant> DeactivateAsync(Guid tenantId, TenantExtensionContext? context = null, CancellationToken ct = default)
        => SetStatusAsync(tenantId, IdentityTenantStatuses.Disabled, context ?? TenantExtensionContext.Create(TenantExtensionOperations.Deactivated), ct);

    public async Task EnsureExtensionAsync(
        Guid tenantId,
        TenantExtensionContext? context = null,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);

        var tenant = await _tenants.FindByIdAsync(tenantId, ct).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"Tenant '{tenantId}' was not found.");

        await _tenantExtensions.EnsureExtensionAsync(
            tenant,
            context ?? TenantExtensionContext.Create(TenantExtensionOperations.Ensure),
            ct).ConfigureAwait(false);
    }

    private async Task<IdentityTenant> SetStatusAsync(
        Guid tenantId,
        string status,
        TenantExtensionContext context,
        CancellationToken ct)
    {
        ValidateTenantId(tenantId);

        var updated = await _tenants.SetStatusAsync(tenantId, status, ct).ConfigureAwait(false);

        if (string.Equals(status, IdentityTenantStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            await _tenantExtensions.OnTenantActivatedAsync(updated, context, ct).ConfigureAwait(false);
            var evt = new TenantActivatedEvent
            {
                TenantId = updated.TenantId,
                TenantName = updated.Name,
                CorrelationId = context.CorrelationId,
                CausationId = context.CausationId,
                TraceId = context.TraceId
            };
            evt.Metadata["idempotencyKey"] = $"{TenantActivatedEvent.TypeName}:{updated.TenantId:D}";
            await InvokeLifecycleAndPublishAsync(evt, (hook, e, token) => hook.OnTenantActivatedAsync(e, token), ct)
                .ConfigureAwait(false);
        }
        else
        {
            await _tenantExtensions.OnTenantDeactivatedAsync(updated, context, ct).ConfigureAwait(false);
            var evt = new TenantDeactivatedEvent
            {
                TenantId = updated.TenantId,
                TenantName = updated.Name,
                CorrelationId = context.CorrelationId,
                CausationId = context.CausationId,
                TraceId = context.TraceId
            };
            evt.Metadata["idempotencyKey"] = $"{TenantDeactivatedEvent.TypeName}:{updated.TenantId:D}";
            await InvokeLifecycleAndPublishAsync(evt, (hook, e, token) => hook.OnTenantDeactivatedAsync(e, token), ct)
                .ConfigureAwait(false);
        }

        return updated;
    }

    private async Task InvokeLifecycleAndPublishAsync<TEvent>(
        TEvent evt,
        Func<IAuthLifecycleHook, TEvent, CancellationToken, Task> hookInvoker,
        CancellationToken ct)
        where TEvent : AuthLifecycleEventBase
    {
        try
        {
            await hookInvoker(_lifecycleHook, evt, ct).ConfigureAwait(false);
            await _eventPublisher.PublishAsync(evt, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit tenant lifecycle event {EventType}. EventId={EventId}", evt.EventType, evt.EventId);
            if (_eventOptions.Value.StrictPublishFailures)
                throw;
        }
    }

    private static void ValidateIdentityTenant(IdentityTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ValidateTenantId(tenant.TenantId);
        _ = NormalizeTenantName(tenant.Name);
    }

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
    }

    private static string NormalizeTenantName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new IdentityValidationException("Tenant name is required.");

        return name.Trim();
    }
}
