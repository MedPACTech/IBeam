namespace IBeam.Management.CustomerIdentity;

/// <summary>Customer-realm operations. Declaring a name neither installs an endpoint nor grants access.</summary>
public static class ManagementCustomerIdentityOperations
{
    public const string Info = "management.identity.info";
    public const string TenantsRead = "management.identity.tenants.read";
    public const string TenantsCreate = "management.identity.tenants.create";
    public const string TenantsUpdate = "management.identity.tenants.update";
    public const string TenantsStatus = "management.identity.tenants.status";
    public const string UsersRead = "management.identity.users.read";
    public const string UsersProvision = "management.identity.users.provision";
    public const string InvitesRead = "management.identity.invites.read";
    public const string InvitesManage = "management.identity.invites.manage";
    public const string MembershipsManage = "management.identity.memberships.manage";
    public const string RolesAssign = "management.identity.roles.assign";
    public const string RolesRead = "management.identity.roles.read";
    public const string OwnersRead = "management.identity.owners.read";
    public const string OwnersTransfer = "management.identity.owners.transfer";
    public const string OnboardingCreate = "management.identity.onboarding.create";
    public const string OnboardingRead = "management.identity.onboarding.read";
    public const string OnboardingRetry = "management.identity.onboarding.retry";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] {
        Info, TenantsRead, TenantsCreate, TenantsUpdate, TenantsStatus, UsersRead, UsersProvision,
        InvitesRead, InvitesManage, MembershipsManage, RolesAssign, RolesRead, OwnersRead, OwnersTransfer, OnboardingCreate, OnboardingRead, OnboardingRetry });
}

public sealed record ManagementCustomerIdentityInfo(string ApplicationKey, string Environment,
    Guid IdentityRealmId, string RealmScope, int ProtocolVersion, IReadOnlyList<string> Capabilities)
{
    /// <summary>Optional read-only reconciliation protocol, independent of operation grants. Zero means unavailable.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int OperationReceiptProtocolVersion { get; init; }
    /// <summary>Native membership owner-protection protocol; zero means unavailable. Does not grant transfer authority.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int OwnershipProtectionProtocolVersion { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int OwnershipTransferProtocolVersion { get; init; }
}

public static class ManagementCustomerIdentityStatuses
{
    public const string Available = "available";
    public const string NotInstalled = "not-installed";
    public const string Incompatible = "incompatible";
    public const string Unavailable = "unavailable";
}

/// <summary>Only validated versioned metadata; never stores an untrusted target error or response body.</summary>
public sealed record ManagementCustomerIdentityDiscovery(string Status, ManagementCustomerIdentityInfo? Info = null);

public sealed record ManagementTargetProfile(ManagementDiagnosticsInfo? Diagnostics, ManagementCustomerIdentityDiscovery CustomerIdentity)
{
    public IReadOnlyList<string> Capabilities => Array.AsReadOnly((Diagnostics?.Capabilities ?? [])
        .Concat(CustomerIdentity.Info?.Capabilities ?? []).Distinct(StringComparer.Ordinal).ToArray());
}

public interface IManagementTargetProfileProbe
{
    Task<ManagementTargetProfile> ProbeProfileAsync(string origin, ManagementSecretValue key, CancellationToken ct = default);
}
public interface IManagementCustomerIdentityInfoService
{
    Task<ManagementCustomerIdentityInfo> GetAsync(CancellationToken ct = default);
}
public interface IManagementCustomerIdentityAvailabilityService
{
    Task<ManagementCustomerIdentityDiscovery> GetAsync(Guid applicationId, CancellationToken ct = default);
}
/// <summary>An installed module must identify the required query/service adapter. Names alone never advertise a capability.</summary>
public interface IManagementCustomerIdentityCapability
{
    string Operation { get; }
    Type RequiredServiceType { get; }
}

public sealed record ManagementCustomerTenantDto(Guid TenantId, string Name, string Status,
    DateTimeOffset? CreatedUtc, DateTimeOffset? UpdatedUtc, string? Version)
{
    public string Source { get; init; } = ManagementTenantSources.Unknown;
    public override string ToString() => "Management customer tenant (identity fields redacted)";
}
public sealed record ManagementCustomerUserDto(Guid TenantId, Guid UserId, string? Email,
    string? FirstName, string? LastName, string Status, IReadOnlyList<Guid> RoleIds, IReadOnlyList<string> RoleNames)
{
    public DateTimeOffset? CreatedUtc { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
    public string? Version { get; init; }
    public override string ToString() => "Management customer user (identity fields redacted)";
}
public sealed record ManagementCustomerDirectoryQuery(int PageSize = 50, string? ContinuationToken = null,
    string? Status = null, string? NamePrefix = null)
{
    public override string ToString() => "Customer directory query (state redacted)";
}
public sealed record ManagementCustomerDirectoryPage<T>(IReadOnlyList<T> Items, int PageSize, string? ContinuationToken);
/// <summary>Optional bounded target query ports. Implementations must enforce the configured realm/app scope.</summary>
public interface IManagementTenantDirectoryQuery
{
    Task<ManagementCustomerDirectoryPage<ManagementCustomerTenantDto>> ListAsync(ManagementCustomerDirectoryQuery query, CancellationToken ct = default);
    Task<ManagementCustomerTenantDto?> FindAsync(Guid tenantId, CancellationToken ct = default);
}
public interface IManagementCustomerUserDirectoryQuery
{
    Task<ManagementCustomerDirectoryPage<ManagementCustomerUserDto>> ListAsync(Guid tenantId, ManagementCustomerDirectoryQuery query, CancellationToken ct = default);
    Task<ManagementCustomerUserDto?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
