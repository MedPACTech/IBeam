namespace IBeam.Billing.Licensing;

public interface IBillingLicenseReconciler
{
    Task<BillingLicenseReconciliationResult> ReconcileAsync(
        Guid tenantId,
        ReconcileBillingLicenseRequest request,
        CancellationToken ct = default);
}
