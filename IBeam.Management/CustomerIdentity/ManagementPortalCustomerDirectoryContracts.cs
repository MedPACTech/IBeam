using System.Text.Json.Serialization;
namespace IBeam.Management.CustomerIdentity;

/// <summary>Server-only selected connection. No browser request accepts these fields.</summary>
public sealed class ManagementCustomerIdentityConnection
{
    public Guid OrganizationId { get; init; }
    public Guid UserId { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid CredentialId { get; init; }
    public Guid IdentityRealmId { get; init; }
    public string ApplicationKey { get; init; } = "";
    public string Environment { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public string Version { get; init; } = "";
    [JsonIgnore] public ManagementSecretValue Key { get; init; } = new("");
    public override string ToString() => "Customer management connection (key redacted)";
}
public interface IManagementCustomerIdentityConnectionResolver
{
    Task<ManagementCustomerIdentityConnection> ResolveAsync(Guid applicationId, string operation, CancellationToken ct = default);
    Task RecheckAsync(ManagementCustomerIdentityConnection connection, string operation, CancellationToken ct = default);
}
public interface IManagementCustomerIdentityClient
{
    Task<ManagementCustomerMembershipResult> SetMembershipAsync(ManagementCustomerIdentityConnection connection, Guid tenantId, Guid userId,
        ChangeManagementCustomerMembership request, string version, string key, CancellationToken ct = default) => throw new ManagementCustomerIdentityUnavailableException();
    Task<ManagementCustomerMembershipResult> SetCustomerRolesAsync(ManagementCustomerIdentityConnection connection, Guid tenantId, Guid userId,
        ChangeManagementCustomerRoles request, string version, string key, CancellationToken ct = default) => throw new ManagementCustomerIdentityUnavailableException();
    Task<IReadOnlyList<ManagementAssignableRoleDto>> ListAssignableRolesAsync(ManagementCustomerIdentityConnection connection, Guid tenantId, CancellationToken ct = default)
        => throw new ManagementCustomerIdentityUnavailableException();
    Task<ManagementCustomerProvisioningResult> ProvisionUserAsync(ManagementCustomerIdentityConnection connection, Guid tenantId, ProvisionManagementCustomerUser request, string key, CancellationToken ct = default)
        => throw new ManagementCustomerIdentityUnavailableException();
    Task<ManagementCustomerInvitationResult> ChangeInvitationAsync(ManagementCustomerIdentityConnection connection, Guid tenantId, Guid inviteId, bool revoke, string key, string version, CancellationToken ct = default)
        => throw new ManagementCustomerIdentityUnavailableException();
    Task<ManagementIdentityOperationDto> CreateOnboardingAsync(ManagementCustomerIdentityConnection connection, CreateManagementEnterpriseOnboarding request,
        string key, CancellationToken ct = default) => throw new ManagementCustomerIdentityUnavailableException();
    Task<ManagementIdentityOperationDto> RetryOnboardingAsync(ManagementCustomerIdentityConnection connection, Guid operationId,
        string key, CancellationToken ct = default) => throw new ManagementCustomerIdentityUnavailableException();
    Task<ManagementCustomerDirectoryPage<T>> ListAsync<T>(ManagementCustomerIdentityConnection connection, Guid? tenantId, ManagementCustomerDirectoryQuery query, CancellationToken ct = default) where T : class;
    Task<T> FindAsync<T>(ManagementCustomerIdentityConnection connection, Guid? tenantId, Guid id, CancellationToken ct = default) where T : class;
    Task<ManagementIdentityOperationDto> GetOperationAsync(ManagementCustomerIdentityConnection connection, Guid operationId, string authority,
        CancellationToken ct = default) => throw new ManagementCustomerIdentityUnavailableException();
    Task<ManagementTenantMutationResult> CreateTenantAsync(ManagementCustomerIdentityConnection connection, CreateManagementCustomerTenant request,
        string idempotencyKey, CancellationToken ct = default) => throw new ManagementCustomerIdentityUnavailableException();
    Task<ManagementTenantMutationResult> RenameTenantAsync(ManagementCustomerIdentityConnection connection, Guid tenantId, RenameManagementCustomerTenant request,
        string expectedVersion, string idempotencyKey, CancellationToken ct = default) => throw new ManagementCustomerIdentityUnavailableException();
    Task<ManagementIdentityOperationDto?> ResolveOperationAsync(ManagementCustomerIdentityConnection connection, ManagementIdentityOperationLookup request,
        string idempotencyKey, string authority, CancellationToken ct = default) => throw new ManagementCustomerIdentityUnavailableException();
}
public interface IManagementPortalCustomerDirectory<T> where T : class
{
    Task<ManagementCustomerDirectoryPage<T>> ListAsync(Guid applicationId, Guid? tenantId, ManagementCustomerDirectoryQuery query, CancellationToken ct = default);
    Task<T> FindAsync(Guid applicationId, Guid? tenantId, Guid id, CancellationToken ct = default);
}
public sealed class ManagementCustomerIdentityResponseException() : Exception("The application returned an invalid customer management response.");
public sealed class ManagementCustomerIdentityRateLimitException() : Exception("Customer management is temporarily rate limited.");
