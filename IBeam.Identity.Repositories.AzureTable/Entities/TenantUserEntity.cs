using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

// TenantUsers table entity: PK = "TEN#{tenantId}", RK = "USR#{userId}"
internal sealed class TenantUserEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Helpful denormalization (avoid parsing keys in code)
    public string TenantId { get; set; } = default!;
    public string UserId { get; set; } = default!;

    // Membership lifecycle
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; }            // set explicitly by store on insert
    public DateTimeOffset? DisabledAt { get; set; }
    public string? DisabledReason { get; set; }

    // Display / lookup helpers (optional but very practical)
    public string? UserDisplayName { get; set; }
    public string? Email { get; set; }                       // normalized lower-case

    // Authorization
    public string RolesCsv { get; set; } = "";
    public string? PermissionsJson { get; set; }
}
