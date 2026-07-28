namespace IBeam.Licensing.Services;

public sealed class LicenseGate : ILicenseGate
{
    private readonly ILicensingStore _store;

    public LicenseGate(ILicensingStore store)
    {
        _store = store;
    }

    public async Task<LicenseGateResult> CheckAsync(LicenseGateRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        TenantLicenseService.ValidateTenantId(request.TenantId);
        var subject = NormalizeSubject(request.Subject);
        var entitlement = TenantLicenseService.NormalizeRequired(request.Entitlement, nameof(request.Entitlement));
        var operationName = TenantLicenseService.NormalizeOptional(request.OperationName);
        var metadata = TenantLicenseService.NormalizeMetadata(request.Metadata);
        var licenses = await _store.ListLicensesAsync(request.TenantId, ct).ConfigureAwait(false);

        if (licenses.Count == 0)
        {
            return LicenseGateResult.Deny(
                request.TenantId,
                subject,
                entitlement,
                operationName,
                LicenseGateDenialCodes.NoLicense,
                $"Tenant '{request.TenantId}' has no licenses.",
                metadata);
        }

        var now = DateTimeOffset.UtcNow;
        var sawRuntimeEligibleLicense = false;
        var sawEntitledLicense = false;
        foreach (var license in licenses)
        {
            var eligibility = license.EvaluateRuntimeEligibility(now);
            if (!eligibility.IsEligible)
                continue;

            sawRuntimeEligibleLicense = true;
            if (!HasEntitlement(license, entitlement))
                continue;

            sawEntitledLicense = true;
            if (license.SeatLimit is null)
                return LicenseGateResult.Allow(request.TenantId, subject, entitlement, operationName, license.LicenseId, metadata);

            var assignments = await _store.ListAssignmentsAsync(request.TenantId, license.LicenseId, ct).ConfigureAwait(false);
            if (assignments.Any(x => SubjectMatches(x.Subject, subject)))
                return LicenseGateResult.Allow(request.TenantId, subject, entitlement, operationName, license.LicenseId, metadata);
        }

        if (!sawRuntimeEligibleLicense)
        {
            return LicenseGateResult.Deny(
                request.TenantId,
                subject,
                entitlement,
                operationName,
                LicenseGateDenialCodes.InactiveLicense,
                $"Tenant '{request.TenantId}' has no runtime-eligible licenses.",
                metadata);
        }

        if (!sawEntitledLicense)
        {
            return LicenseGateResult.Deny(
                request.TenantId,
                subject,
                entitlement,
                operationName,
                LicenseGateDenialCodes.MissingEntitlement,
                $"Tenant '{request.TenantId}' does not have entitlement '{entitlement}'.",
                metadata);
        }

        return LicenseGateResult.Deny(
            request.TenantId,
            subject,
            entitlement,
            operationName,
            LicenseGateDenialCodes.MissingSeat,
            $"Subject '{subject.SubjectType}:{subject.SubjectId}' does not have a seat for entitlement '{entitlement}'.",
            metadata);
    }

    public async Task RequireAsync(LicenseGateRequest request, CancellationToken ct = default)
    {
        var result = await CheckAsync(request, ct).ConfigureAwait(false);
        if (!result.Allowed)
            throw new LicensingException(result.Reason ?? $"License entitlement '{result.Entitlement}' is required.");
    }

    private static LicenseSubject NormalizeSubject(LicenseSubject subject)
    {
        var subjectType = TenantLicenseService.NormalizeRequired(subject.SubjectType, "subjectType");
        var subjectId = TenantLicenseService.NormalizeRequired(subject.SubjectId, "subjectId");
        return new LicenseSubject(subjectType, subjectId, TenantLicenseService.NormalizeOptional(subject.DisplayName));
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
