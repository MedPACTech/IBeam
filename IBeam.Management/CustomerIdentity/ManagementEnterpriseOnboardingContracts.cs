using System.Text.Json.Serialization;
namespace IBeam.Management.CustomerIdentity;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateManagementEnterpriseOnboarding
{
    public string TenantName { get; init; } = "";
    public string InitialAdminEmail { get; init; } = "";
    public string? ExternalReference { get; init; }
    public string PolicyKey { get; init; } = "";
    public override string ToString() => "Enterprise onboarding request (identity fields redacted)";
}
/// <summary>Status is business progress. Processing receipts succeeding means setup finished, never customer acceptance.</summary>
public sealed record ManagementEnterpriseOnboardingDto(Guid OperationId, Guid OnboardingId, Guid? TenantId, Guid InviteId,
    string Status, string Step, string DeliveryStatus, bool Completed, string? ErrorCode,
    DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc)
{
    public string NextAction => Completed ? "none" : Status == "awaiting-acceptance" ? "await-customer" : "poll";
}
public interface IManagementEnterpriseOnboardingCommandService
{
    Task<ManagementIdentityOperationDto> CreateAsync(CreateManagementEnterpriseOnboarding request, string key, CancellationToken ct = default);
    Task<ManagementIdentityOperationDto> RetryAsync(Guid operationId, string key, CancellationToken ct = default);
}
public interface IManagementPortalEnterpriseOnboardingService
{
    Task<ManagementPortalOperationResult> CreateAsync(Guid applicationId, CreateManagementEnterpriseOnboarding request, string key, CancellationToken ct = default);
    Task<ManagementPortalOperationResult> RetryAsync(Guid applicationId, Guid operationId, string key, CancellationToken ct = default);
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RetryManagementEnterpriseOnboarding;
public interface IManagementEnterpriseOnboardingService
{
    Task<ManagementEnterpriseOnboardingDto> CreateAsync(CreateManagementEnterpriseOnboarding request, string idempotencyKey, CancellationToken ct = default);
    Task<ManagementEnterpriseOnboardingDto> GetAsync(Guid onboardingId, string authority, CancellationToken ct = default);
    Task<ManagementEnterpriseOnboardingDto> RetryAsync(Guid onboardingId, string idempotencyKey, CancellationToken ct = default);
}
/// <summary>Private encrypted recovery input is persisted separately from metadata-only operation/audit receipts.</summary>
public sealed record ManagementEnterpriseOnboardingEntity
{
    public required ManagementIdentityOperationScope Scope { get; init; }
    public required string ApplicationKey { get; init; }
    public required string Environment { get; init; }
    public required Guid OperationId { get; init; }
    public required Guid OnboardingId { get; init; }
    public required Guid InviteId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public Guid? TenantId { get; init; }
    public string Step { get; init; } = "tenant-creation";
    public string DeliveryStatus { get; init; } = "pending";
    public Guid? DeliveryClaim { get; init; }
    public DateTimeOffset? DeliveryLeaseUtc { get; init; }
    public int DeliveryAttempts { get; init; }
    public string? ErrorCode { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required DateTimeOffset ExpiresUtc { get; init; }
    [JsonIgnore] public string EncryptedInput { get; init; } = "";
    public string Version { get; init; } = "";
    public override string ToString() => "Enterprise onboarding journal (recovery input redacted)";
}
public interface IManagementEnterpriseOnboardingRepository
{
    Task<ManagementEnterpriseOnboardingEntity?> GetAsync(ManagementIdentityOperationScope scope, Guid id, CancellationToken ct = default);
    Task<bool> TryCreateAsync(ManagementEnterpriseOnboardingEntity entity, CancellationToken ct = default);
    Task<ManagementEnterpriseOnboardingEntity> ReplaceAsync(ManagementEnterpriseOnboardingEntity entity, string expectedVersion, CancellationToken ct = default);
}
