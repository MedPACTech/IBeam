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
    Guid TenantId, Guid ActorUserId, Guid OperationId);

/// <summary>Safe contract metadata only. Discovery is evidence of installation, never an authorization grant.</summary>
public sealed record ManagementOperationCapability(string Operation, int ProtocolVersion, string RequestSchema,
    int RequestSchemaVersion, string ResultSchema, int ResultSchemaVersion, int ReceiptProtocolVersion);
