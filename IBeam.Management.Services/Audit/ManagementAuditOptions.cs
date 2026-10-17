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
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) {
        "management.identity.read", "management.identity.bootstrap", "management.diagnostics.info",
        "management.diagnostics.errors.read", "management.diagnostics.logs.read",
        "management.credentials.authenticate", "management.credentials.issue", "management.credentials.rotate", "management.credentials.revoke",
        "management.applications.list", "management.applications.read", "management.applications.create", "management.applications.update",
        "management.applications.connection.read", "management.applications.connection.activate", "management.applications.connection.replace",
        "management.applications.connection.configure", "management.applications.connection.test",
        "management.application-access.list", "management.application-access.grant", "management.application-access.revoke", "management.application-access.require",
        "management.support-invitations.create", "management.support-invitations.accept", "management.support-users.disable",
        "management.portal.errors.list", "management.portal.errors.read", "management.portal.logs.list", "management.portal.logs.read", "management.portal.applications.list"
    };
    public static bool IsManagementService(object service) => service.GetType().Assembly.GetName().Name is
        "IBeam.Management.Services" or "IBeam.Management.Identity.Services";
}
