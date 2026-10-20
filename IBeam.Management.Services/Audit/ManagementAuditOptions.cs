using System.Text.RegularExpressions;
using IBeam.Services.Abstractions;
namespace IBeam.Management.Services.Audit;
public sealed class ManagementAuditOptions
{
    public const string SectionName = "IBeam:Management:Audit";
    public int RetentionDays { get; set; } = 90;
    public bool FailOnWriteError { get; set; } = true;
    public int WriteTimeoutSeconds { get; set; } = 5;
    public bool IsValid() => RetentionDays is >= 1 and <= 3650 && WriteTimeoutSeconds is >= 1 and <= 30;
}
public sealed class ManagementAuditContext : IManagementAuditContext
{
    public string CorrelationId { get; private set; } = Guid.NewGuid().ToString("N");
    public void AcceptMachineCorrelation(string value)
    {
        if (Regex.IsMatch(value ?? "", "^[a-f0-9]{32}$")) CorrelationId = value!;
    }
}
/// <summary>Explicit allowlist prevents arbitrary action/message text being persisted.</summary>
public static class ManagementAuditOperations
{
    public static readonly IReadOnlySet<string> All = Create();
    private static IReadOnlySet<string> Create()
    {
    var actions = new HashSet<string>(StringComparer.Ordinal) {
        "management.identity.read", "management.identity.bootstrap", "management.diagnostics.info",
        "management.identity.info", "management.portal.customer-identity.info", "management.portal.customer-identity.authorize",
        "management.portal.customer-identity.connection", "management.portal.customer-identity.connection-check",
        "management.portal.customer-identity.tenants.list", "management.portal.customer-identity.tenants.read",
        "management.portal.customer-identity.users.list", "management.portal.customer-identity.users.read",
        "management.portal.customer-identity.invites.list", "management.portal.customer-identity.invites.read",
        "management.portal.customer-identity.delegate", "management.identity.delegation.validate", "management.identity.delegation.require",
        "management.identity.tenants.read",
        "management.identity.tenants.create", "management.identity.tenants.update", "management.identity.tenants.status",
        "management.identity.onboarding.create", "management.identity.onboarding.read", "management.identity.onboarding.retry",
        "management.identity.tenants.provenance", "management.identity.roles.catalog", "management.identity.users.provision",
        "management.identity.memberships.manage", "management.identity.roles.assign",
        "management.identity.invites.manage.resend", "management.identity.invites.manage.revoke", "management.identity.invitations.operation.read",
        "management.portal.customer-identity.roles.catalog", "management.portal.customer-identity.onboarding.policies",
        "management.portal.customer-identity.onboarding.create", "management.portal.customer-identity.onboarding.retry",
        "management.portal.customer-identity.onboarding.create.unknown", "management.portal.customer-identity.onboarding.retry.unknown",
        "management.portal.customer-identity.onboarding.create.in-progress", "management.portal.customer-identity.onboarding.retry.in-progress",
        "management.portal.customer-identity.onboarding.create.recoverable-failure", "management.portal.customer-identity.onboarding.retry.recoverable-failure",
        "management.portal.customer-identity.onboarding.create.awaiting-acceptance", "management.portal.customer-identity.onboarding.retry.awaiting-acceptance",
        "management.portal.customer-identity.onboarding.create.completed", "management.portal.customer-identity.onboarding.retry.completed",
        "management.identity.operations.execute", "management.identity.operations.read", "management.identity.operations.audit",
        "management.identity.operations.resolve",
        "management.portal.customer-identity.operations.read",
        "management.portal.customer-identity.operations.reconcile",
        "management.portal.customer-identity.operations.reconcile.succeeded", "management.portal.customer-identity.operations.reconcile.unknown",
        "management.portal.customer-identity.operations.reconcile.pending", "management.portal.customer-identity.operations.reconcile.running",
        "management.portal.customer-identity.operations.reconcile.uncertain",
        "management.portal.customer-identity.tenants.create", "management.portal.customer-identity.tenants.update",
        "management.portal.customer-identity.tenants.create.succeeded", "management.portal.customer-identity.tenants.update.succeeded",
        "management.portal.customer-identity.tenants.create.unknown", "management.portal.customer-identity.tenants.update.unknown",
        "management.portal.customer-identity.tenants.create.pending", "management.portal.customer-identity.tenants.update.pending",
        "management.portal.customer-identity.tenants.create.running", "management.portal.customer-identity.tenants.update.running",
        "management.portal.customer-identity.tenants.create.uncertain", "management.portal.customer-identity.tenants.update.uncertain",
        "management.identity.users.read", "management.identity.invites.read", "management.identity.tenant-scope.require",
        "management.diagnostics.errors.read", "management.diagnostics.logs.read",
        "management.credentials.authenticate", "management.credentials.issue", "management.credentials.rotate", "management.credentials.revoke",
        "management.applications.list", "management.applications.read", "management.applications.create", "management.applications.update",
        "management.applications.connection.read", "management.applications.connection.activate", "management.applications.connection.replace",
        "management.applications.connection.configure", "management.applications.connection.test",
        "management.application-access.list", "management.application-access.grant", "management.application-access.revoke", "management.application-access.require",
        "management.support-invitations.create", "management.support-invitations.accept", "management.support-users.disable",
        "management.portal.errors.list", "management.portal.errors.read", "management.portal.logs.list", "management.portal.logs.read", "management.portal.applications.list"
    };
    foreach (var action in new[] { "users.provision", "invites.resend", "invites.revoke", "memberships.manage", "roles.assign", "tenants.status" }) {
        actions.Add("management.portal.customer-identity." + action);
        foreach (var status in new[] { "pending", "running", "succeeded", "uncertain", "sending", "sent", "failed", "unknown", "accepted", "redeemed", "expired", "revoked", "superseded", "not-required" })
            actions.Add("management.portal.customer-identity." + action + "." + status);
    }
    return actions;
    }
    public static bool IsManagementService(object service) => service.GetType().Assembly.GetName().Name is
        "IBeam.Management.Services" or "IBeam.Management.Identity.Services" or "IBeam.Management.Identity.Target.Services";
}
