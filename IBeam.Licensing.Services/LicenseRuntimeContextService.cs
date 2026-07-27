namespace IBeam.Licensing.Services;

public sealed class LicenseRuntimeContextService : ILicenseRuntimeContextService
{
    private readonly ILicensingStore _store;

    public LicenseRuntimeContextService(ILicensingStore store)
    {
        _store = store;
    }

    public async Task<LicenseRuntimeContextInfo> GetRuntimeContextAsync(
        Guid tenantId,
        GetLicenseRuntimeContextRequest request,
        CancellationToken ct = default)
    {
        TenantLicenseService.ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var subject = NormalizeSubject(request.Subject);
        var licenses = await _store.ListLicensesAsync(tenantId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var entitlements = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var seatStates = new List<LicenseRuntimeSeatInfo>();
        var summaries = new List<LicenseRuntimeSummaryInfo>();
        var anyRuntimeEligible = false;
        var anyInactive = false;
        var anyMissingSeat = false;

        foreach (var license in licenses)
        {
            var eligibility = license.EvaluateRuntimeEligibility(now);
            if (!eligibility.IsEligible)
            {
                anyInactive = true;
                if (request.IncludeAdminDetails)
                    summaries.Add(ToSummary(license, eligibility.RuntimeStatus));
                continue;
            }

            anyRuntimeEligible = true;
            if (request.IncludeAdminDetails)
                summaries.Add(ToSummary(license, eligibility.RuntimeStatus));

            if (license.SeatLimit is null)
            {
                seatStates.Add(new LicenseRuntimeSeatInfo(
                    license.LicenseId,
                    license.PlanKey,
                    LicenseRuntimeSeatStates.NotRequired,
                    license.SeatLimit));
                AddEntitlements(entitlements, license);
                continue;
            }

            var assignments = await _store.ListAssignmentsAsync(tenantId, license.LicenseId, ct).ConfigureAwait(false);
            if (assignments.Any(x => SubjectMatches(x.Subject, subject)))
            {
                seatStates.Add(new LicenseRuntimeSeatInfo(
                    license.LicenseId,
                    license.PlanKey,
                    LicenseRuntimeSeatStates.Assigned,
                    license.SeatLimit));
                AddEntitlements(entitlements, license);
            }
            else
            {
                anyMissingSeat = true;
                seatStates.Add(new LicenseRuntimeSeatInfo(
                    license.LicenseId,
                    license.PlanKey,
                    LicenseRuntimeSeatStates.Missing,
                    license.SeatLimit));
            }
        }

        var status = ResolveStatus(licenses.Count, entitlements.Count, anyRuntimeEligible, anyInactive, anyMissingSeat);
        return new LicenseRuntimeContextInfo(
            tenantId,
            subject,
            status,
            entitlements.Count > 0,
            string.Equals(status, LicenseRuntimeContextStatuses.Limited, StringComparison.OrdinalIgnoreCase),
            string.Equals(status, LicenseRuntimeContextStatuses.ContactAdmin, StringComparison.OrdinalIgnoreCase),
            entitlements.ToList(),
            seatStates,
            licenses.Where(x => x.EvaluateRuntimeEligibility(now).IsEligible).Select(x => x.ExpiresUtc).Where(x => x.HasValue).Min(),
            licenses.Where(x => x.EvaluateRuntimeEligibility(now).IsEligible).Select(x => x.GraceEndsUtc).Where(x => x.HasValue).Max(),
            [],
            request.IncludeAdminDetails ? summaries : []);
    }

    private static string ResolveStatus(
        int licenseCount,
        int entitlementCount,
        bool anyRuntimeEligible,
        bool anyInactive,
        bool anyMissingSeat)
    {
        if (licenseCount == 0)
            return LicenseRuntimeContextStatuses.Unlicensed;

        if (entitlementCount > 0)
            return anyInactive ? LicenseRuntimeContextStatuses.Limited : LicenseRuntimeContextStatuses.Active;

        if (anyMissingSeat)
            return LicenseRuntimeContextStatuses.ContactAdmin;

        return anyRuntimeEligible
            ? LicenseRuntimeContextStatuses.ContactAdmin
            : LicenseRuntimeContextStatuses.Expired;
    }

    private static void AddEntitlements(ISet<string> entitlements, TenantLicenseRecord license)
    {
        foreach (var entitlement in license.Entitlements)
            entitlements.Add(entitlement);
    }

    private static LicenseRuntimeSummaryInfo ToSummary(TenantLicenseRecord license, string runtimeStatus)
        => new(
            license.LicenseId,
            license.PlanKey,
            license.DisplayName,
            runtimeStatus,
            license.CommercialStatus,
            license.Entitlements,
            license.SeatLimit,
            license.StartsUtc,
            license.ExpiresUtc,
            license.GraceEndsUtc);

    private static LicenseSubject NormalizeSubject(LicenseSubject subject)
    {
        var subjectType = TenantLicenseService.NormalizeRequired(subject.SubjectType, "subjectType");
        var subjectId = TenantLicenseService.NormalizeRequired(subject.SubjectId, "subjectId");
        return new LicenseSubject(subjectType, subjectId, TenantLicenseService.NormalizeOptional(subject.DisplayName));
    }

    private static bool SubjectMatches(LicenseSubject assigned, LicenseSubject requested)
        => string.Equals(assigned.SubjectType, requested.SubjectType, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(assigned.SubjectId, requested.SubjectId, StringComparison.OrdinalIgnoreCase);
}
