using IBeam.Management.Operations;
using System.Security.Cryptography;
using System.Text.Json;

namespace IBeam.Management.CustomerIdentity;

public sealed record ManagementBulkInvitationSelection(Guid InviteId, string ExpectedVersion);
public sealed record ManagementBulkInvitationRevokeRequest(ManagementBulkInvitationSelection[] Invitations);
public sealed record ManagementBulkInvitationItemResult(Guid InviteId, Guid OperationId, string State, string? FailureCode);
public sealed record ManagementBulkInvitationRevokeResult(Guid TenantId, Guid BatchId, Guid OperationId, string Outcome,
    string State, int Total, bool Complete, ManagementBulkInvitationItemResult[] Items);

public static class ManagementBulkInvitationRevokeOperation
{
    public const string Name = "management.invites.bulk-revoke";
    public const string PortalCommand = "api/management/applications/{applicationId:guid}/tenants/{tenantId:guid}/invitation-revocations/{batchId:guid}";
    public const string PortalReceipt = PortalCommand + "/operations/{operationId:guid}";
    public static ManagementOperationDescriptor Descriptor => new(Name, "POST",
        "/api/management-operations/tenants/{tenantId:guid}/invitation-revocations/{resourceId:guid}",
        new(typeof(ManagementBulkInvitationRevokeRequest), "ibeam.invites.bulk-revoke.request"),
        new(typeof(ManagementBulkInvitationRevokeResult), "ibeam.invites.bulk-revoke.result"),
        typeof(IManagementOperationHandler<ManagementBulkInvitationRevokeRequest, ManagementBulkInvitationRevokeResult>), [Name], 1, Name,
        MaximumRequestBytes: 32768, MaximumResultBytes: 32768);
    public static bool Exact(string? value) => value is { Length: > 0 and <= 256 } && value != "*" && value.Trim() == value && !value.Any(char.IsControl);
    public static void Validate(ManagementBulkInvitationRevokeRequest request)
    {
        if (request?.Invitations is not { Length: > 0 and <= 50 } values || values.Any(x => x is null || x.InviteId == Guid.Empty || !Exact(x.ExpectedVersion)) ||
            values.Select(x => x.InviteId).Distinct().Count() != values.Length) throw new ManagementOperationInvalidException();
    }
    public static bool Terminal(string state) => state is "revoked" or "already-revoked" or "not-eligible" or "changed-version";
    public static Guid ItemOperationId(Guid batch, Guid invite) => new(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Protocol = "ibeam.invites.bulk-revoke.v1", batch, invite })).AsSpan(0, 16));
    public static ManagementBulkInvitationRevokeResult Project(ManagementBulkInvitationRevokeResult value, Guid tenant, Guid batch, Guid operation)
    {
        if (value is null || value.TenantId != tenant || value.BatchId != batch || value.OperationId != operation || tenant == Guid.Empty || batch == Guid.Empty || operation == Guid.Empty ||
            value.Outcome is not ("accepted" or "found" or "unknown") || value.Total is < 0 or > 50 || value.Items is null || value.Items.Length != value.Total ||
            value.Items.Any(x => x is null || x.InviteId == Guid.Empty || x.OperationId != ItemOperationId(operation, x.InviteId) || x.State is not ("pending" or "unknown" or "failed" or "revoked" or "already-revoked" or "not-eligible" or "changed-version") ||
                (x.State is "pending" or "revoked" or "already-revoked") && x.FailureCode is not null ||
                x.State == "not-eligible" && x.FailureCode is not ("accepted" or "expired" or "missing") ||
                x.State == "changed-version" && x.FailureCode != "version-changed" ||
                x.State == "failed" && x.FailureCode is not ("rate-limited" or "rejected") ||
                x.State == "unknown" && x.FailureCode is not (null or "provider" or "deadline" or "cancelled")) ||
            value.Items.Select(x => x.InviteId).Distinct().Count() != value.Total || value.Items.Select(x => x.OperationId).Distinct().Count() != value.Total ||
            value.Total == 0 && (value.Outcome != "unknown" || value.Complete || value.State != "unknown") ||
            value.Total > 0 && (value.Complete != value.Items.All(x => Terminal(x.State)) || value.State != State(value.Items)))
            throw new ManagementOperationUnavailableException();
        return value with { Items = value.Items.Select(x => x with { }).ToArray() };
    }
    public static string State(IReadOnlyList<ManagementBulkInvitationItemResult> items) => items.Any(x => x.State == "unknown") ? "unknown" :
        items.All(x => x.State is "revoked" or "already-revoked") ? "succeeded" : "partial";
}

/// <summary>Private target journal metadata. Never return it directly or attach it to audit snapshots.</summary>
public sealed record ManagementBulkInvitationItem(Guid InviteId, Guid OperationId, string ExpectedVersion, string State, string? FailureCode);
public sealed record ManagementBulkInvitationBatch(ManagementOperationContext Scope, string ApplicationKey, string Environment,
    DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, string Fingerprint, ManagementBulkInvitationItem[] Items)
{
    public string Version { get; init; } = "";
    public override string ToString() => "Private bulk invitation journal (selection and versions redacted)";
}
public interface IManagementBulkInvitationBatchRepository
{
    int ProtocolVersion { get; }
    bool Durable { get; }
    Task<ManagementBulkInvitationBatch?> GetAsync(ManagementOperationContext scope, CancellationToken ct = default);
    Task<bool> TryCreateAsync(ManagementBulkInvitationBatch row, CancellationToken ct = default);
    Task<ManagementBulkInvitationBatch> ReplaceAsync(ManagementBulkInvitationBatch row, string expectedVersion, CancellationToken ct = default);
}
public interface IManagementBulkInvitationRevokeService
{
    Task<ManagementBulkInvitationRevokeResult> ExecuteAsync(ManagementOperationContext scope, ManagementBulkInvitationRevokeRequest request, CancellationToken ct = default);
    Task<ManagementBulkInvitationRevokeResult> ReadReceiptAsync(ManagementOperationContext scope, CancellationToken ct = default);
}
public interface IManagementPortalBulkInvitationRevokeService
{
    Task<ManagementBulkInvitationRevokeResult> ExecuteAsync(Guid applicationId, Guid tenantId, Guid batchId, ManagementBulkInvitationRevokeRequest request,
        string idempotencyKey, string tenantVersion, CancellationToken ct = default);
    Task<ManagementBulkInvitationRevokeResult> ReadReceiptAsync(Guid applicationId, Guid tenantId, Guid batchId, Guid operationId, CancellationToken ct = default);
}
