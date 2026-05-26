using Azure;
using Azure.Data.Tables;

namespace IBeam.Identity.Repositories.AzureTable.Entities;

// UserTenants table entity: PK = "USR#{userId}", RK = "TEN#{tenantId}"
internal sealed class UserTenantEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Helpful denormalization (avoid parsing keys in code)
    public string UserId { get; set; } = default!;
    public string TenantId { get; set; } = default!;

    // Membership lifecycle
    public string Status { get; set; } = "Active";             // "Active" | "Invited" | "Disabled" | etc.
    public DateTimeOffset CreatedAt { get; set; }              // when membership created
    public DateTimeOffset? DisabledAt { get; set; }            // optional
    public string? DisabledReason { get; set; }                // optional

    // Display
    public string? TenantDisplayName { get; set; }             // optional (if you don’t have a separate Tenants table)
    public string? UserDisplayName { get; set; }               // optional (sometimes useful for tenant->users queries)

    // Authorization
    public string RolesCsv { get; set; } = "";                 // keep for now; migrate later to JSON or separate table
    public string RoleIdsCsv { get; set; } = "";
    public string? PermissionsJson { get; set; }               // optional (future-proofing)

    // Default tenant selection (per-user)
    public bool IsDefault { get; set; }
    public DateTimeOffset? LastSelectedAt { get; set; }
}
