namespace IBeam.Billing.Licensing;

public interface IBillingLicenseReconciler
{
    Task<BillingLicenseReconciliationResult> ReconcileAsync(
        Guid tenantId,
        ReconcileBillingLicenseRequest request,
        CancellationToken ct = default);
}

public interface IBillingProviderMigrationService
{
    Task<BillingProviderMigrationInfo> MigrateAsync(
        Guid tenantId,
        MigrateBillingProviderRequest request,
        CancellationToken ct = default);
}
