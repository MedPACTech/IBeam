using IBeam.Management.Operations;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using IBeam.Management.Entities;
using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.Audit;
/// <summary>Metadata-only sink. Never copies generic transaction snapshots, actor/header context or exception text.</summary>
public sealed class ManagementAuditSink(IManagementAuditRepository repository, IManagementPrincipalProvider principals,
    IManagementAuditContext context, IOptions<ManagementAuditOptions> options, IOptions<ManagementDiagnosticsOptions> deployment,
    TimeProvider clock) : IAuditTrailSink
{
    private readonly IManagementDelegatedActorContext? _delegation;
    public ManagementAuditSink(IManagementAuditRepository repository, IManagementPrincipalProvider principals,
        IManagementAuditContext context, IOptions<ManagementAuditOptions> options, IOptions<ManagementDiagnosticsOptions> deployment,
        TimeProvider clock, IManagementDelegatedActorContext delegation)
        : this(repository, principals, context, options, deployment, clock) => _delegation = delegation;
    private readonly ManagementOperationRegistry _registry = ManagementOperationRegistry.BuiltIn;
    public ManagementAuditSink(IManagementAuditRepository repository, IManagementPrincipalProvider principals,
        IManagementAuditContext context, IOptions<ManagementAuditOptions> options, IOptions<ManagementDiagnosticsOptions> deployment,
        TimeProvider clock, ManagementOperationRegistry registry)
        : this(repository, principals, context, options, deployment, clock) => _registry = registry;
    public ManagementAuditSink(IManagementAuditRepository repository, IManagementPrincipalProvider principals,
        IManagementAuditContext context, IOptions<ManagementAuditOptions> options, IOptions<ManagementDiagnosticsOptions> deployment,
        TimeProvider clock, IManagementDelegatedActorContext delegation, ManagementOperationRegistry registry)
        : this(repository, principals, context, options, deployment, clock, delegation) => _registry = registry;
    private static readonly Meter Meter = new("IBeam.Management.Audit");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("management.audit.write_failures");
    public async Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default)
    {
        if (!ManagementAuditOperations.All.Contains(transaction.Action) && !_registry.AllowsAudit(transaction.Action)) return;
        var now = clock.GetUtcNow();
        Guid? org = null, actor = null, credential = null; string subject = "unknown";
        var identities = principals.GetPrincipal()?.Identities.Where(x => x.IsAuthenticated).ToArray() ?? [];
        if (identities.Length == 1)
        {
            var identity = identities[0];
            var types = identity.FindAll(ManagementClaimTypes.SubjectType).Select(x => x.Value).ToArray();
            if (types.Length == 1 && types[0] == ManagementSubjectTypes.Human)
            {
                org = UniqueGuid(identity, ManagementClaimTypes.OrganizationId);
                var ids = identity.Claims.Where(x => x.Type is "uid" or "sub" || x.Type == ClaimTypes.NameIdentifier).Select(x => x.Value).Distinct(StringComparer.Ordinal).ToArray();
                if (ids.Length == 1 && Guid.TryParse(ids[0], out var id) && id != Guid.Empty && org.HasValue) { actor = id; subject = ManagementSubjectTypes.Human; }
            }
            else if (types.Length == 1 && types[0] == ManagementSubjectTypes.Credential)
            {
                var app = identity.FindAll(ManagementClaimTypes.ApplicationKey).Select(x => x.Value).ToArray();
                var env = identity.FindAll(ManagementClaimTypes.Environment).Select(x => x.Value).ToArray();
                if (app.Length == 1 && env.Length == 1 && app[0] == deployment.Value.ApplicationKey && env[0] == deployment.Value.Environment)
                { credential = UniqueGuid(identity, ManagementClaimTypes.CredentialId); if (credential.HasValue) subject = ManagementSubjectTypes.Credential; }
            }
        }
        // TenantId is assigned by trusted management identity services for invitation/bootstrap calls without a session.
        if (!org.HasValue && transaction.Action is "management.support-invitations.accept" or "management.support-invitations.create" or "management.support-users.disable" or "management.identity.bootstrap")
            org = transaction.TenantId is { } tenant && tenant != Guid.Empty ? tenant : null;
        var isApp = transaction.Action.StartsWith("management.applications.", StringComparison.Ordinal) ||
            transaction.Action.StartsWith("management.application-access.", StringComparison.Ordinal) ||
            transaction.Action.StartsWith("management.portal.errors.", StringComparison.Ordinal) || transaction.Action.StartsWith("management.portal.logs.", StringComparison.Ordinal) ||
            transaction.Action.StartsWith("management.portal.customer-identity.", StringComparison.Ordinal);
        var delegated = _delegation?.Current;
        if (delegated is not null && delegated.ApplicationKey == deployment.Value.ApplicationKey && delegated.Environment == deployment.Value.Environment &&
            (!credential.HasValue || credential.Value == delegated.CredentialId))
        {
            org = delegated.OrganizationId; actor = delegated.UserId; credential = delegated.CredentialId; subject = ManagementSubjectTypes.Credential;
        }
        else delegated = null;
        var entry = new ManagementAuditEntity { AuditId = Guid.NewGuid(), OccurredUtc = now, ExpiresUtc = now.AddDays(options.Value.RetentionDays),
            OrganizationId = org, ActorUserId = actor, CredentialId = credential, SubjectType = subject,
            ApplicationId = delegated?.ApplicationId ?? (isApp ? transaction.EntityId : null),
            SubjectId = transaction.Action.StartsWith("management.portal.customer-identity.tenants.create.", StringComparison.Ordinal) ||
                transaction.Action.StartsWith("management.portal.customer-identity.onboarding.", StringComparison.Ordinal) ||
                transaction.Action.StartsWith("management.portal.customer-identity.tenants.update.", StringComparison.Ordinal) ||
                transaction.Action.StartsWith("management.portal.customer-identity.tenants.status.", StringComparison.Ordinal) ||
                transaction.Action.StartsWith("management.portal.customer-identity.users.provision.", StringComparison.Ordinal) ||
                transaction.Action.StartsWith("management.portal.customer-identity.invites.resend.", StringComparison.Ordinal) ||
                transaction.Action.StartsWith("management.portal.customer-identity.invites.revoke.", StringComparison.Ordinal) ||
                transaction.Action.StartsWith("management.portal.customer-identity.memberships.manage.", StringComparison.Ordinal) ||
                transaction.Action.StartsWith("management.portal.customer-identity.roles.assign.", StringComparison.Ordinal) ||
                transaction.Action.StartsWith("management.portal.customer-identity.owners.transfer.", StringComparison.Ordinal) ||
                transaction.Action.StartsWith("management.portal.customer-identity.operations.reconcile.", StringComparison.Ordinal)
                ? !transaction.Action.EndsWith(".unknown", StringComparison.Ordinal) && transaction.TenantId is { } target && target != Guid.Empty ? target : null :
                transaction.Action == "management.support-users.disable" || delegated is not null && transaction.Action.StartsWith("management.identity.", StringComparison.Ordinal)
                    ? transaction.EntityId : null,
            ApplicationKey = subject == ManagementSubjectTypes.Credential ? deployment.Value.ApplicationKey : null,
            Environment = subject == ManagementSubjectTypes.Credential ? deployment.Value.Environment : null,
            Operation = transaction.Action, Succeeded = transaction.Succeeded, CorrelationId = context.CorrelationId };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.WriteTimeoutSeconds));
        try { await repository.AppendAsync(entry, deadline.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { Failures.Add(1); if (options.Value.FailOnWriteError) throw new ManagementAuditUnavailableException(); }
    }
    private static Guid? UniqueGuid(ClaimsIdentity identity, string type)
    { var values = identity.FindAll(type).ToArray(); return values.Length == 1 && Guid.TryParse(values[0].Value, out var id) && id != Guid.Empty ? id : null; }
    public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default) => Task.CompletedTask;
}
