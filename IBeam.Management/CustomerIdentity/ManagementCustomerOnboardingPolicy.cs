namespace IBeam.Management.CustomerIdentity;

/// <summary>Target-owned policy. Initializer keys and role templates never come from portal request JSON.</summary>
public sealed record ManagementCustomerOnboardingPolicy(string Key, string InitialAdministratorRole, string? InitializerKey = null);
public sealed record ManagementCustomerOnboardingPolicyDto(string Key, bool RequiresInitialization);
public interface IManagementCustomerOnboardingPolicy
{
    Task<IReadOnlyList<ManagementCustomerOnboardingPolicy>> GetEnabledAsync(CancellationToken ct = default);
}
public sealed record ManagementCustomerInitializationContext(Guid IdentityRealmId, Guid OrganizationId, Guid ApplicationId,
    Guid OperationId, Guid OnboardingId, Guid TenantId, string PolicyKey);
public enum ManagementCustomerInitializationState { Absent, Applied, Unknown }
/// <summary>Consumers must atomically deduplicate Initialize by operation/tenant and durably reconcile after lost acknowledgements.</summary>
public interface IManagementCustomerOnboardingInitializer
{
    string Key { get; }
    Task<ManagementCustomerInitializationState> ReconcileAsync(ManagementCustomerInitializationContext context, CancellationToken ct = default);
    Task InitializeAsync(ManagementCustomerInitializationContext context, CancellationToken ct = default);
}
public interface IManagementCustomerOnboardingPolicyCatalog
{
    Task<IReadOnlyList<ManagementCustomerOnboardingPolicyDto>> ListAsync(CancellationToken ct = default);
}
