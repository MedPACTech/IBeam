using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Identity.Target.Services.Onboarding;

/// <summary>Bridge for an extension store whose atomic Create commits its complete required initialization.
/// Multi-step integrations must implement their own durable reconciliation rather than treating row existence as completion.</summary>
public sealed class TenantExtensionOnboardingInitializer<TTenant>(string key, IIdentityTenantStore tenants,
    ITenantExtensionStore<TTenant> extensions) : IManagementCustomerOnboardingInitializer where TTenant : class, IIdentityTenantExtension
{
    public string Key => key;
    public async Task<ManagementCustomerInitializationState> ReconcileAsync(ManagementCustomerInitializationContext context, CancellationToken ct = default)
    {
        var row = await extensions.FindByTenantIdAsync(context.TenantId, ct);
        return row is null ? ManagementCustomerInitializationState.Absent : row.TenantId == context.TenantId
            ? ManagementCustomerInitializationState.Applied : ManagementCustomerInitializationState.Unknown;
    }
    public async Task InitializeAsync(ManagementCustomerInitializationContext context, CancellationToken ct = default)
    {
        if (await ReconcileAsync(context, ct) != ManagementCustomerInitializationState.Absent) return;
        var tenant = await tenants.FindByIdAsync(context.TenantId, ct);
        if (tenant is null || !tenant.IsActive) throw new ManagementCustomerIdentityUnavailableException();
        var row = await extensions.CreateAsync(tenant, new TenantExtensionContext(TenantExtensionOperations.Ensure,
            CausationId: context.OperationId.ToString("D"), Metadata: new Dictionary<string, string> {
                ["management.onboarding-id"] = context.OnboardingId.ToString("D"), ["management.policy-key"] = context.PolicyKey
            }) { RedactFailureDetails = true, RequireLifecycleDelivery = true }, ct);
        if (row.TenantId != context.TenantId) throw new ManagementCustomerIdentityUnavailableException();
    }
}
