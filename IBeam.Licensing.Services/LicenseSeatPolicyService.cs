using IBeam.Services.Abstractions;

namespace IBeam.Licensing.Services;

[IBeamOperation("licensing.seat-policies")]
public sealed class LicenseSeatPolicyService : ILicenseSeatPolicyService
{
    private readonly ITenantLicenseService _licenses;
    private readonly ILicenseSeatAssignmentService _assignments;
    private readonly IServiceOperationExecutor _operations;

    public LicenseSeatPolicyService(
        ITenantLicenseService licenses,
        ILicenseSeatAssignmentService assignments,
        IServiceOperationExecutor? operations = null)
    {
        _licenses = licenses;
        _assignments = assignments;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("licensing.seat-policies.grant-single-user")]
    public async Task<TenantLicenseWithSeatsInfo> GrantSingleUserLicenseAsync(
        Guid tenantId,
        GrantSingleUserLicenseRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => GrantSingleUserLicenseCoreAsync(tenantId, request, createdByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    private async Task<TenantLicenseWithSeatsInfo> GrantSingleUserLicenseCoreAsync(
        Guid tenantId,
        GrantSingleUserLicenseRequest request,
        Guid? createdByUserId,
        CancellationToken ct)
    {
        TenantLicenseService.ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var subject = NormalizeSubject(request.Subject);
        var licenseRequest = CloneLicenseRequest(request.License);
        licenseRequest.SeatLimit = 1;

        var license = await _licenses.GrantLicenseAsync(tenantId, licenseRequest, createdByUserId, ct).ConfigureAwait(false);
        var assignment = await _assignments.AssignSeatAsync(
            tenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest
            {
                Subject = subject,
                Metadata = new Dictionary<string, string>(
                    TenantLicenseService.NormalizeMetadata(request.SeatMetadata),
                    StringComparer.OrdinalIgnoreCase)
            },
            createdByUserId,
            ct).ConfigureAwait(false);

        return new TenantLicenseWithSeatsInfo(license, [assignment]);
    }

    [IBeamOperation("licensing.seat-policies.grant-tenant-seats")]
    public async Task<TenantLicenseWithSeatsInfo> GrantTenantSeatLicenseAsync(
        Guid tenantId,
        GrantTenantSeatLicenseRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => GrantTenantSeatLicenseCoreAsync(tenantId, request, createdByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    private async Task<TenantLicenseWithSeatsInfo> GrantTenantSeatLicenseCoreAsync(
        Guid tenantId,
        GrantTenantSeatLicenseRequest request,
        Guid? createdByUserId,
        CancellationToken ct)
    {
        TenantLicenseService.ValidateTenantId(tenantId);
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var subjects = request.InitialSubjects
            .Select(NormalizeSubject)
            .DistinctBy(x => (x.SubjectType.ToUpperInvariant(), x.SubjectId.ToUpperInvariant()))
            .ToList();

        var requestedSeatLimit = request.SeatLimit ?? request.License.SeatLimit;
        if (requestedSeatLimit <= 0)
            throw new LicensingException("A tenant seat license requires a positive seat limit.");

        if (subjects.Count > requestedSeatLimit)
            throw new LicensingException($"Initial seat assignments exceed the requested seat limit of {requestedSeatLimit}.");

        var licenseRequest = CloneLicenseRequest(request.License);
        licenseRequest.SeatLimit = requestedSeatLimit;

        var license = await _licenses.GrantLicenseAsync(tenantId, licenseRequest, createdByUserId, ct).ConfigureAwait(false);
        var assignments = new List<LicenseSeatAssignmentInfo>();
        foreach (var subject in subjects)
        {
            var assignment = await _assignments.AssignSeatAsync(
                tenantId,
                license.LicenseId,
                new AssignLicenseSeatRequest
                {
                    Subject = subject,
                    Metadata = new Dictionary<string, string>(
                        TenantLicenseService.NormalizeMetadata(request.SeatMetadata),
                        StringComparer.OrdinalIgnoreCase)
                },
                createdByUserId,
                ct).ConfigureAwait(false);
            assignments.Add(assignment);
        }

        return new TenantLicenseWithSeatsInfo(license, assignments);
    }

    private static LicenseSubject NormalizeSubject(LicenseSubject subject)
    {
        var subjectType = TenantLicenseService.NormalizeRequired(subject.SubjectType, "subjectType");
        var subjectId = TenantLicenseService.NormalizeRequired(subject.SubjectId, "subjectId");
        return new LicenseSubject(subjectType, subjectId, TenantLicenseService.NormalizeOptional(subject.DisplayName));
    }

    private static GrantTenantLicenseRequest CloneLicenseRequest(GrantTenantLicenseRequest source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        return new GrantTenantLicenseRequest
        {
            PlanKey = source.PlanKey,
            DisplayName = source.DisplayName,
            Status = source.Status,
            Entitlements = [.. source.Entitlements],
            Limits = new Dictionary<string, int>(source.Limits, StringComparer.OrdinalIgnoreCase),
            SeatLimit = source.SeatLimit,
            StartsUtc = source.StartsUtc,
            ExpiresUtc = source.ExpiresUtc,
            ProviderName = source.ProviderName,
            ProviderCustomerId = source.ProviderCustomerId,
            ProviderSubscriptionId = source.ProviderSubscriptionId,
            ProviderPriceId = source.ProviderPriceId,
            ProviderStatus = source.ProviderStatus,
            CommercialStatus = source.CommercialStatus,
            GraceEndsUtc = source.GraceEndsUtc,
            Metadata = new Dictionary<string, string>(source.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }
}
