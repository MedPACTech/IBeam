using IBeam.Services.Abstractions;

namespace IBeam.Licensing.Services;

[IBeamOperation("licensing.licenses")]
public sealed class TenantLicenseService : ITenantLicenseService
{
    private readonly ILicensingStore _store;
    private readonly ILicensePlanCatalogProvider _plans;
    private readonly IServiceOperationExecutor _operations;

    public TenantLicenseService(
        ILicensingStore store,
        ILicensePlanCatalogProvider plans,
        IServiceOperationExecutor? operations = null)
    {
        _store = store;
        _plans = plans;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("licensing.licenses.list")]
    public async Task<IReadOnlyList<TenantLicenseInfo>> ListTenantLicensesAsync(Guid tenantId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ListTenantLicensesCoreAsync(tenantId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<TenantLicenseInfo>> ListTenantLicensesCoreAsync(Guid tenantId, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        var licenses = await _store.ListLicensesAsync(tenantId, ct).ConfigureAwait(false);
        return licenses.Select(TenantLicenseInfo.FromRecord).ToList();
    }

    [IBeamOperation("licensing.licenses.grant")]
    public async Task<TenantLicenseInfo> GrantLicenseAsync(
        Guid tenantId,
        GrantTenantLicenseRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => GrantLicenseCoreAsync(tenantId, request, createdByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    private async Task<TenantLicenseInfo> GrantLicenseCoreAsync(
        Guid tenantId,
        GrantTenantLicenseRequest request,
        Guid? createdByUserId,
        CancellationToken ct)
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
        var status = NormalizeLicenseStatus(request.Status) ?? LicenseStatuses.Active;
        var commercialStatus = NormalizeCommercialStatus(request.CommercialStatus)
                               ?? InferCommercialStatus(status, request);

        if (request.ExpiresUtc is { } expires && expires <= starts)
            throw new LicensingException("expiresUtc must be after startsUtc.");

        ValidateGraceWindow(starts, request.ExpiresUtc, request.GraceEndsUtc);

        var record = new TenantLicenseRecord(
            LicenseId: Guid.NewGuid(),
            TenantId: tenantId,
            PlanKey: planKey,
            DisplayName: NormalizeOptional(request.DisplayName) ?? plan?.DisplayName ?? planKey,
            Status: status,
            Entitlements: entitlements,
            Limits: limits,
            SeatLimit: request.SeatLimit ?? plan?.DefaultSeatLimit ?? ReadSeatLimit(limits),
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
            Metadata: NormalizeMetadata(request.Metadata),
            CommercialStatus: commercialStatus,
            GraceEndsUtc: request.GraceEndsUtc);

        var saved = await _store.UpsertLicenseAsync(record, ct).ConfigureAwait(false);
        return TenantLicenseInfo.FromRecord(saved);
    }

    [IBeamOperation("licensing.licenses.update")]
    public async Task<TenantLicenseInfo> UpdateLicenseAsync(
        Guid tenantId,
        Guid licenseId,
        UpdateTenantLicenseRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => UpdateLicenseCoreAsync(tenantId, licenseId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = licenseId },
            ct).ConfigureAwait(false);

    private async Task<TenantLicenseInfo> UpdateLicenseCoreAsync(
        Guid tenantId,
        Guid licenseId,
        UpdateTenantLicenseRequest request,
        CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        ValidateLicenseId(licenseId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var existing = await GetRequiredAsync(tenantId, licenseId, ct).ConfigureAwait(false);
        var limits = request.Limits is null ? existing.Limits : NormalizeLimits(request.Limits);
        var status = NormalizeLicenseStatus(request.Status) ?? existing.Status;
        var commercialStatus = NormalizeCommercialStatus(request.CommercialStatus) ?? existing.CommercialStatus;
        var updated = existing with
        {
            DisplayName = NormalizeOptional(request.DisplayName) ?? existing.DisplayName,
            Status = status,
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
            CommercialStatus = commercialStatus,
            GraceEndsUtc = request.GraceEndsUtc ?? existing.GraceEndsUtc,
            Metadata = request.Metadata is null ? existing.Metadata : NormalizeMetadata(request.Metadata)
        };

        if (updated.ExpiresUtc is { } expires && expires <= updated.StartsUtc)
            throw new LicensingException("expiresUtc must be after startsUtc.");

        ValidateGraceWindow(updated.StartsUtc, updated.ExpiresUtc, updated.GraceEndsUtc);

        var saved = await _store.UpsertLicenseAsync(updated, ct).ConfigureAwait(false);
        return TenantLicenseInfo.FromRecord(saved);
    }

    [IBeamOperation("licensing.licenses.revoke")]
    public async Task RevokeLicenseAsync(Guid tenantId, Guid licenseId, string? reason, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => RevokeLicenseCoreAsync(tenantId, licenseId, reason, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = licenseId },
            ct).ConfigureAwait(false);

    private async Task RevokeLicenseCoreAsync(Guid tenantId, Guid licenseId, string? reason, CancellationToken ct)
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

    internal static string? NormalizeLicenseStatus(string? value)
    {
        var normalized = NormalizeOptional(value)?.ToLowerInvariant();
        if (normalized is null)
            return null;

        return normalized switch
        {
            LicenseStatuses.Active => normalized,
            LicenseStatuses.Trialing => normalized,
            LicenseStatuses.Grace => normalized,
            LicenseStatuses.Manual => normalized,
            LicenseStatuses.Suspended => normalized,
            LicenseStatuses.Revoked => normalized,
            LicenseStatuses.Expired => normalized,
            _ => throw new LicensingException($"License status '{value}' is not supported.")
        };
    }

    internal static string? NormalizeCommercialStatus(string? value)
    {
        var normalized = NormalizeOptional(value)?.ToLowerInvariant();
        if (normalized is null)
            return null;

        return normalized switch
        {
            LicenseCommercialStatuses.Unknown => normalized,
            LicenseCommercialStatuses.Paid => normalized,
            LicenseCommercialStatuses.Trial => normalized,
            LicenseCommercialStatuses.Grace => normalized,
            LicenseCommercialStatuses.PastDue => normalized,
            LicenseCommercialStatuses.Canceled => normalized,
            LicenseCommercialStatuses.Manual => normalized,
            LicenseCommercialStatuses.SupportGranted => normalized,
            _ => throw new LicensingException($"License commercial status '{value}' is not supported.")
        };
    }

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

    private static string InferCommercialStatus(string status, GrantTenantLicenseRequest request)
    {
        if (string.Equals(status, LicenseStatuses.Trialing, StringComparison.OrdinalIgnoreCase))
            return LicenseCommercialStatuses.Trial;

        if (string.Equals(status, LicenseStatuses.Grace, StringComparison.OrdinalIgnoreCase))
            return LicenseCommercialStatuses.Grace;

        if (string.Equals(status, LicenseStatuses.Manual, StringComparison.OrdinalIgnoreCase))
            return LicenseCommercialStatuses.Manual;

        if (string.Equals(status, LicenseStatuses.Suspended, StringComparison.OrdinalIgnoreCase))
            return LicenseCommercialStatuses.PastDue;

        if (string.Equals(status, LicenseStatuses.Expired, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, LicenseStatuses.Revoked, StringComparison.OrdinalIgnoreCase))
            return LicenseCommercialStatuses.Canceled;

        return HasProviderReference(request)
            ? LicenseCommercialStatuses.Paid
            : LicenseCommercialStatuses.Manual;
    }

    private static bool HasProviderReference(GrantTenantLicenseRequest request)
        => !string.IsNullOrWhiteSpace(request.ProviderName) ||
           !string.IsNullOrWhiteSpace(request.ProviderCustomerId) ||
           !string.IsNullOrWhiteSpace(request.ProviderSubscriptionId) ||
           !string.IsNullOrWhiteSpace(request.ProviderPriceId) ||
           !string.IsNullOrWhiteSpace(request.ProviderStatus);

    private static void ValidateGraceWindow(DateTimeOffset startsUtc, DateTimeOffset? expiresUtc, DateTimeOffset? graceEndsUtc)
    {
        if (graceEndsUtc is null)
            return;

        if (graceEndsUtc <= startsUtc)
            throw new LicensingException("graceEndsUtc must be after startsUtc.");

        if (expiresUtc is { } expires && graceEndsUtc <= expires)
            throw new LicensingException("graceEndsUtc must be after expiresUtc.");
    }
}
