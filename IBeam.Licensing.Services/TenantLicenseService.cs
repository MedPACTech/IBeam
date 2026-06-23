namespace IBeam.Licensing.Services;

public sealed class TenantLicenseService : ITenantLicenseService
{
    private readonly ILicensingStore _store;
    private readonly ILicensePlanCatalogProvider _plans;

    public TenantLicenseService(ILicensingStore store, ILicensePlanCatalogProvider plans)
    {
        _store = store;
        _plans = plans;
    }

    public async Task<IReadOnlyList<TenantLicenseInfo>> ListTenantLicensesAsync(Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var licenses = await _store.ListLicensesAsync(tenantId, ct).ConfigureAwait(false);
        return licenses.Select(TenantLicenseInfo.FromRecord).ToList();
    }

    public async Task<TenantLicenseInfo> GrantLicenseAsync(
        Guid tenantId,
        GrantTenantLicenseRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var planKey = NormalizeRequired(request.PlanKey, "planKey");
        var plan = await _plans.GetPlanAsync(planKey, ct).ConfigureAwait(false);
        var entitlements = MergeEntitlements(plan?.Entitlements, request.Entitlements);
        var limits = MergeLimits(plan?.Limits, request.Limits);
        var now = DateTimeOffset.UtcNow;
        var starts = request.StartsUtc ?? now;

        if (request.ExpiresUtc is { } expires && expires <= starts)
            throw new LicensingException("expiresUtc must be after startsUtc.");

        var record = new TenantLicenseRecord(
            LicenseId: Guid.NewGuid(),
            TenantId: tenantId,
            PlanKey: planKey,
            DisplayName: NormalizeOptional(request.DisplayName) ?? plan?.DisplayName ?? planKey,
            Status: NormalizeOptional(request.Status) ?? LicenseStatuses.Active,
            Entitlements: entitlements,
            Limits: limits,
            SeatLimit: request.SeatLimit ?? ReadSeatLimit(limits),
            StartsUtc: starts,
            ExpiresUtc: request.ExpiresUtc,
            CreatedUtc: now,
            CreatedByUserId: createdByUserId == Guid.Empty ? null : createdByUserId,
            RevokedUtc: null,
            RevocationReason: null,
            ProviderName: NormalizeOptional(request.ProviderName),
            ProviderCustomerId: NormalizeOptional(request.ProviderCustomerId),
            ProviderSubscriptionId: NormalizeOptional(request.ProviderSubscriptionId),
            ProviderPriceId: NormalizeOptional(request.ProviderPriceId),
            ProviderStatus: NormalizeOptional(request.ProviderStatus),
            Metadata: NormalizeMetadata(request.Metadata));

        var saved = await _store.UpsertLicenseAsync(record, ct).ConfigureAwait(false);
        return TenantLicenseInfo.FromRecord(saved);
    }

    public async Task<TenantLicenseInfo> UpdateLicenseAsync(
        Guid tenantId,
        Guid licenseId,
        UpdateTenantLicenseRequest request,
        CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateLicenseId(licenseId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var existing = await GetRequiredAsync(tenantId, licenseId, ct).ConfigureAwait(false);
        var limits = request.Limits is null ? existing.Limits : NormalizeLimits(request.Limits);
        var updated = existing with
        {
            DisplayName = NormalizeOptional(request.DisplayName) ?? existing.DisplayName,
            Status = NormalizeOptional(request.Status) ?? existing.Status,
            Entitlements = request.Entitlements is null ? existing.Entitlements : NormalizeEntitlements(request.Entitlements),
            Limits = limits,
            SeatLimit = request.SeatLimit ?? existing.SeatLimit,
            StartsUtc = request.StartsUtc ?? existing.StartsUtc,
            ExpiresUtc = request.ExpiresUtc ?? existing.ExpiresUtc,
            ProviderName = NormalizeOptional(request.ProviderName) ?? existing.ProviderName,
            ProviderCustomerId = NormalizeOptional(request.ProviderCustomerId) ?? existing.ProviderCustomerId,
            ProviderSubscriptionId = NormalizeOptional(request.ProviderSubscriptionId) ?? existing.ProviderSubscriptionId,
            ProviderPriceId = NormalizeOptional(request.ProviderPriceId) ?? existing.ProviderPriceId,
            ProviderStatus = NormalizeOptional(request.ProviderStatus) ?? existing.ProviderStatus,
            Metadata = request.Metadata is null ? existing.Metadata : NormalizeMetadata(request.Metadata)
        };

        if (updated.ExpiresUtc is { } expires && expires <= updated.StartsUtc)
            throw new LicensingException("expiresUtc must be after startsUtc.");

        var saved = await _store.UpsertLicenseAsync(updated, ct).ConfigureAwait(false);
        return TenantLicenseInfo.FromRecord(saved);
    }

    public async Task RevokeLicenseAsync(Guid tenantId, Guid licenseId, string? reason, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateLicenseId(licenseId);

        var existing = await GetRequiredAsync(tenantId, licenseId, ct).ConfigureAwait(false);
        var revoked = existing with
        {
            Status = LicenseStatuses.Revoked,
            RevokedUtc = DateTimeOffset.UtcNow,
            RevocationReason = NormalizeOptional(reason)
        };

        await _store.UpsertLicenseAsync(revoked, ct).ConfigureAwait(false);
    }

    internal async Task<TenantLicenseRecord> GetRequiredAsync(Guid tenantId, Guid licenseId, CancellationToken ct)
    {
        var existing = await _store.GetLicenseAsync(tenantId, licenseId, ct).ConfigureAwait(false);
        return existing ?? throw new LicensingException($"License '{licenseId}' was not found for tenant '{tenantId}'.");
    }

    internal static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new LicensingException("tenantId is required.");
    }

    internal static void ValidateLicenseId(Guid licenseId)
    {
        if (licenseId == Guid.Empty)
            throw new LicensingException("licenseId is required.");
    }

    internal static string NormalizeRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new LicensingException($"{name} is required.");

        return value.Trim();
    }

    internal static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IReadOnlyList<string> NormalizeEntitlements(IEnumerable<string>? values)
        => (values ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static IReadOnlyDictionary<string, int> NormalizeLimits(IReadOnlyDictionary<string, int>? values)
        => (values ?? new Dictionary<string, int>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value, StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? values)
        => (values ?? new Dictionary<string, string>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> MergeEntitlements(IEnumerable<string>? planValues, IEnumerable<string>? requestValues)
        => NormalizeEntitlements((planValues ?? Array.Empty<string>()).Concat(requestValues ?? Array.Empty<string>()));

    private static IReadOnlyDictionary<string, int> MergeLimits(
        IReadOnlyDictionary<string, int>? planValues,
        IReadOnlyDictionary<string, int>? requestValues)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in NormalizeLimits(planValues))
            result[item.Key] = item.Value;
        foreach (var item in NormalizeLimits(requestValues))
            result[item.Key] = item.Value;
        return result;
    }

    private static int? ReadSeatLimit(IReadOnlyDictionary<string, int> limits)
        => limits.TryGetValue("Seats", out var seats) ? seats : null;
}
