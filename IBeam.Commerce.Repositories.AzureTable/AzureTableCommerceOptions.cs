namespace IBeam.Commerce.Repositories.AzureTable;

public sealed class AzureTableCommerceOptions
{
    public const string SectionName = "IBeam:Commerce:AzureTable";

    public string StorageConnectionString { get; set; } = string.Empty;
    public string TablePrefix { get; set; } = string.Empty;
    public string LicensesTableName { get; set; } = "Licenses";
    public string LicenseAssignmentsTableName { get; set; } = "LicenseAssignments";
    public string BillingCustomersTableName { get; set; } = "BillingCustomers";
    public string BillingSubscriptionsTableName { get; set; } = "BillingSubscriptions";
    public string BillingInvoicesTableName { get; set; } = "BillingInvoices";
    public string BillingEventsTableName { get; set; } = "BillingEvents";
    public string BillingPurchasesTableName { get; set; } = "BillingPurchases";
    public string BillingCheckoutAttemptsTableName { get; set; } = "BillingCheckoutAttempts";
    public string BillingPurchaseClaimsTableName { get; set; } = "BillingPurchaseClaims";
    public string BillingProviderBindingsTableName { get; set; } = "BillingProviderBindings";
    public string CreditLedgerTableName { get; set; } = "CreditLedger";
    public string CreditReservationsTableName { get; set; } = "CreditReservations";
    public bool CreateTablesIfNotExists { get; set; } = true;

    public string FullTableName(string tableName)
        => $"{TablePrefix}{tableName}";

    public void Validate()
    {
        StorageConnectionString = (StorageConnectionString ?? string.Empty).Trim();
        TablePrefix = (TablePrefix ?? string.Empty).Trim();
        LicensesTableName = NormalizeOrDefault(LicensesTableName, "Licenses");
        LicenseAssignmentsTableName = NormalizeOrDefault(LicenseAssignmentsTableName, "LicenseAssignments");
        BillingCustomersTableName = NormalizeOrDefault(BillingCustomersTableName, "BillingCustomers");
        BillingSubscriptionsTableName = NormalizeOrDefault(BillingSubscriptionsTableName, "BillingSubscriptions");
        BillingInvoicesTableName = NormalizeOrDefault(BillingInvoicesTableName, "BillingInvoices");
        BillingEventsTableName = NormalizeOrDefault(BillingEventsTableName, "BillingEvents");
        BillingPurchasesTableName = NormalizeOrDefault(BillingPurchasesTableName, "BillingPurchases");
        BillingCheckoutAttemptsTableName = NormalizeOrDefault(BillingCheckoutAttemptsTableName, "BillingCheckoutAttempts");
        BillingPurchaseClaimsTableName = NormalizeOrDefault(BillingPurchaseClaimsTableName, "BillingPurchaseClaims");
        BillingProviderBindingsTableName = NormalizeOrDefault(BillingProviderBindingsTableName, "BillingProviderBindings");
        CreditLedgerTableName = NormalizeOrDefault(CreditLedgerTableName, "CreditLedger");
        CreditReservationsTableName = NormalizeOrDefault(CreditReservationsTableName, "CreditReservations");

        if (string.IsNullOrWhiteSpace(StorageConnectionString))
            throw new InvalidOperationException("AzureTableCommerceOptions.StorageConnectionString is required.");

        ValidateTableName(FullTableName(LicensesTableName), nameof(LicensesTableName));
        ValidateTableName(FullTableName(LicenseAssignmentsTableName), nameof(LicenseAssignmentsTableName));
        ValidateTableName(FullTableName(BillingCustomersTableName), nameof(BillingCustomersTableName));
        ValidateTableName(FullTableName(BillingSubscriptionsTableName), nameof(BillingSubscriptionsTableName));
        ValidateTableName(FullTableName(BillingInvoicesTableName), nameof(BillingInvoicesTableName));
        ValidateTableName(FullTableName(BillingEventsTableName), nameof(BillingEventsTableName));
        ValidateTableName(FullTableName(BillingPurchasesTableName), nameof(BillingPurchasesTableName));
        ValidateTableName(FullTableName(BillingCheckoutAttemptsTableName), nameof(BillingCheckoutAttemptsTableName));
        ValidateTableName(FullTableName(BillingPurchaseClaimsTableName), nameof(BillingPurchaseClaimsTableName));
        ValidateTableName(FullTableName(BillingProviderBindingsTableName), nameof(BillingProviderBindingsTableName));
        ValidateTableName(FullTableName(CreditLedgerTableName), nameof(CreditLedgerTableName));
        ValidateTableName(FullTableName(CreditReservationsTableName), nameof(CreditReservationsTableName));
    }

    public string TenantPk(Guid tenantId) => $"TEN|{tenantId:D}";
    public string LicenseRk(Guid licenseId) => $"LIC|{licenseId:D}";
    public string LicenseAssignmentRk(Guid licenseId, Guid assignmentId) => $"LIC|{licenseId:D}|ASN|{assignmentId:D}";
    public string BillingCustomerRk(Guid customerId) => $"CUS|{customerId:D}";
    public string BillingSubscriptionRk(Guid subscriptionId) => $"SUB|{subscriptionId:D}";
    public string BillingInvoiceRk(Guid invoiceId) => $"INV|{invoiceId:D}";
    public string BillingEventPk(Guid? tenantId) => tenantId is { } id && id != Guid.Empty ? TenantPk(id) : "GLOBAL";
    public string BillingEventRk(Guid eventId) => $"EVT|{eventId:D}";
    public string BillingEventIdempotencyPk() => "IDEMPOTENCY";
    public string BillingEventIdempotencyRk(string idempotencyKey) => $"EVT|{idempotencyKey.ToLowerInvariant()}";
    public string BillingPurchasePk() => "PURCHASES";
    public string BillingPurchaseRk(Guid purchaseId) => $"PUR|{purchaseId:D}";
    public string BillingPurchaseIndexPk() => BillingPurchasePk();
    public string BillingPurchaseIndexRk(string indexType, string value) => $"{indexType}|{Hash(value)}";
    public string BillingCheckoutAttemptPk(Guid purchaseId) => $"PUR|{purchaseId:D}";
    public string BillingCheckoutAttemptRk(Guid attemptId) => $"ATT|{attemptId:D}";
    public string BillingClaimPk() => "CLAIMS";
    public string BillingClaimRk(Guid claimId) => $"CLM|{claimId:D}";
    public string BillingClaimIndexPk() => BillingClaimPk();
    public string BillingClaimIndexRk(string indexType, string value) => $"{indexType}|{Hash(value)}";
    public string BillingProviderBindingPk(Guid tenantId) => TenantPk(tenantId);
    public string BillingProviderBindingRk(Guid subscriptionId, Guid bindingId) => $"SUB|{subscriptionId:D}|BND|{bindingId:D}";
    public string BillingProviderMigrationRk(Guid subscriptionId, string providerName, string idempotencyKey)
        => $"SUB|{subscriptionId:D}|MIG|{Hash($"{providerName}:{idempotencyKey}")}";
    public string CreditLedgerRk(Guid ledgerEntryId) => $"LED|{ledgerEntryId:D}";
    public string CreditReservationRk(Guid reservationId) => $"RES|{reservationId:D}";
    public string CreditReservationIdempotencyPk(Guid tenantId) => $"IDEMPOTENCY|{tenantId:D}";
    public string CreditReservationIdempotencyRk(string idempotencyKey) => $"RES|{idempotencyKey.ToLowerInvariant()}";

    private static string NormalizeOrDefault(string? value, string defaultValue)
        => string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();

    private static void ValidateTableName(string name, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"{propertyName} is required.");
        if (name.Length < 3 || name.Length > 63)
            throw new InvalidOperationException($"{propertyName} must be 3-63 characters.");
        if (name.Any(x => !char.IsLetterOrDigit(x)))
            throw new InvalidOperationException($"{propertyName} must be alphanumeric only (Azure Tables rule).");
    }

    private static string Hash(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())));
}
