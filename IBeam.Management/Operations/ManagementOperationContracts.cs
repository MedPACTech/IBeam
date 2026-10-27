namespace IBeam.Management.Operations;

/// <summary>Reserved families, unavailable until the host installs a typed handler and explicitly grants access.</summary>
public static class ManagementTenantDataOperations
{
    public const string Export = "management.tenants.export";
    public const string Delete = "management.tenants.delete";
}

/// <summary>A small JSON contract, registered by trusted host code. Version 1 is the only supported schema protocol.</summary>
public sealed record ManagementOperationSchema(Type ContractType, string Id, int Version = 1);

/// <summary>One fixed route per descriptor. Resource slots are nonempty GUIDs; URLs and routes never come from a caller.</summary>
public sealed record ManagementOperationDescriptor(string Operation, string Method, string RouteTemplate,
    ManagementOperationSchema Request, ManagementOperationSchema Result, Type HandlerServiceType,
    IReadOnlyList<string> RequiredGrants, int ReceiptProtocolVersion, string AuditAction,
    int MaximumRequestBytes = 16384, int MaximumResultBytes = 65536, int ProtocolVersion = 1);

/// <summary>Provider-neutral app port. Authentication, live grants, scope, receipts and audit belong to the management pipeline.</summary>
public interface IManagementOperationHandler<TRequest, TResult>
{
    Task<TResult> ExecuteAsync(ManagementOperationContext context, TRequest request, CancellationToken ct = default);
}

/// <summary>No profile, customer principal, arbitrary headers or credentials cross this boundary.</summary>
public sealed record ManagementOperationContext(Guid OrganizationId, Guid ApplicationId, Guid RealmId,
    Guid TenantId, Guid ActorUserId, Guid OperationId)
{
    public Guid ResourceId { get; init; }
    public string? ResourceVersion { get; init; }
}

/// <summary>Target boundary: compare the presented scope with the current verified actor, machine grants and live host policy.
/// Registration must supply a compatible implementation; an app handler or request cannot create this authority.</summary>
public interface IManagementOperationAccessService
{
    int ProtocolVersion { get; }
    Task RequireAsync(string operation, ManagementOperationContext scope, CancellationToken ct = default);
}

/// <summary>Portal port for typed registered contracts. Implementation owns live access, signing, fixed transport and tracking.</summary>
public interface IManagementOperationClient
{
    Task<TResult> ExecuteAsync<TRequest, TResult>(Guid applicationId, string operation, Guid tenantId, Guid resourceId,
        TRequest request, string idempotencyKey, string? expectedVersion, CancellationToken ct = default);
    Task<TResult> ReadReceiptAsync<TResult>(Guid applicationId, string operation, Guid tenantId, Guid resourceId,
        Guid operationId, CancellationToken ct = default) => throw new ManagementOperationUnavailableException();
}
public sealed class ManagementOperationInvalidException() : Exception("The management operation request is invalid.");
public sealed class ManagementOperationPreconditionException() : Exception("The resource version changed.");
public sealed class ManagementOperationConflictException() : Exception("The management operation conflicts with current state.");
public sealed class ManagementOperationUnavailableException() : Exception("The management operation is unavailable.");
/// <summary>A target may have committed without returning a trustworthy reply. Contains scoped metadata only; never retry automatically.</summary>
public sealed class ManagementOperationUncertainException(Guid applicationId, Guid tenantId, Guid resourceId, Guid operationId)
    : Exception("The management operation outcome is unknown.")
{
    public Guid ApplicationId { get; } = applicationId;
    public Guid TenantId { get; } = tenantId;
    public Guid ResourceId { get; } = resourceId;
    public Guid OperationId { get; } = operationId;
}

/// <summary>Safe contract metadata only. Discovery is evidence of installation, never an authorization grant.</summary>
public sealed record ManagementOperationCapability(string Operation, int ProtocolVersion, string RequestSchema,
    int RequestSchemaVersion, string ResultSchema, int ResultSchemaVersion, int ReceiptProtocolVersion);
