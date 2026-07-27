namespace IBeam.Licensing.Services;

public sealed class LicenseAuthorizer : ILicenseAuthorizer
{
    private readonly ILicensingStore _store;

    public LicenseAuthorizer(ILicensingStore store)
    {
        _store = store;
    }

    public async Task<LicenseAuthorizationResult> AuthorizeAsync(
        Guid tenantId,
        LicenseSubject subject,
        string entitlement,
        CancellationToken ct = default)
    {
        TenantLicenseService.ValidateTenantId(tenantId);
        var normalizedEntitlement = TenantLicenseService.NormalizeRequired(entitlement, nameof(entitlement));
        var licenses = await _store.ListLicensesAsync(tenantId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var denialReasons = new List<string>();

        foreach (var license in licenses)
        {
            var eligibility = license.EvaluateRuntimeEligibility(now);
            if (!eligibility.IsEligible)
            {
                denialReasons.Add($"{license.LicenseId}: {eligibility.RuntimeStatus}");
                continue;
            }

            if (!HasEntitlement(license, normalizedEntitlement))
                continue;

            if (license.SeatLimit is null)
                return LicenseAuthorizationResult.Allow(license.LicenseId);

            var assignments = await _store.ListAssignmentsAsync(tenantId, license.LicenseId, ct).ConfigureAwait(false);
            if (assignments.Any(x => SubjectMatches(x.Subject, subject)))
                return LicenseAuthorizationResult.Allow(license.LicenseId);
        }

        var reasonSuffix = denialReasons.Count == 0
            ? string.Empty
            : $" Checked licenses: {string.Join(", ", denialReasons)}.";
        return LicenseAuthorizationResult.Deny($"Tenant '{tenantId}' does not have a runtime-eligible license entitlement for '{normalizedEntitlement}'.{reasonSuffix}");
    }

    private static bool HasEntitlement(TenantLicenseRecord license, string entitlement)
        => license.Entitlements.Any(x =>
            string.Equals(x, entitlement, StringComparison.OrdinalIgnoreCase) ||
            (x.EndsWith(":*", StringComparison.Ordinal) &&
             entitlement.StartsWith(x[..^1], StringComparison.OrdinalIgnoreCase)));

    private static bool SubjectMatches(LicenseSubject assigned, LicenseSubject requested)
        => string.Equals(assigned.SubjectType, requested.SubjectType, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(assigned.SubjectId, requested.SubjectId, StringComparison.OrdinalIgnoreCase);
}
