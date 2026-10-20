using System.Text;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.CustomerIdentity;

internal sealed class OperationFixture : IManagementCustomerIdentityTargetAccessService, IManagementIdentityOperationAuditRepository
{
    public OperationStore Store { get; } = new();
    public OperationClock Clock { get; } = new();
    public ManagementDelegatedActor Actor { get; set; } = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "atlas", "Testing", ManagementCustomerIdentityOperations.TenantsCreate, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1), new string('a',32));
    public bool Allowed { get; set; } = true;
    public bool AuditOutage { get; set; }
    public bool FailResults { get; set; }
    public Dictionary<Guid, ManagementAuditEntity> Events { get; } = [];
    public ManagementIdentityOperationOptions Options { get; } = new() { FingerprintKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) };
    public IManagementIdentityOperationService Service => new ManagementIdentityOperationService(Store, this, this,
        Microsoft.Extensions.Options.Options.Create(Options), Clock, new ServiceOperationExecutor());
    public ManagementIdentityMutationRequest Request(string payload = "{\"name\":\"secret-customer@example.test\"}", string key = "request-12345678", Guid resource = default, Guid? existing = null)
        => new() { Operation = Actor.Operation, ResourceScope = resource, ExistingResourceId = existing, IdempotencyKey = key, CanonicalPayload = Encoding.UTF8.GetBytes(payload) };
    public Task<ManagementDelegatedActor> RequireAsync(string operation, CancellationToken ct = default)
        => Allowed && Actor.Operation == operation ? Task.FromResult(Actor) : Task.FromException<ManagementDelegatedActor>(new ManagementAccessDeniedException());
    public Task AppendOnceAsync(ManagementAuditEntity entry, CancellationToken ct = default)
    {
        if (AuditOutage || FailResults && !entry.Operation.EndsWith(".intent", StringComparison.Ordinal)) throw new Exception("provider-secret-canary");
        lock (Events) {
            if (Events.TryGetValue(entry.AuditId, out var old)) Assert.AreEqual(old, entry);
            else Events.Add(entry.AuditId, entry);
        }
        return Task.CompletedTask;
    }
}
internal sealed class OperationClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
internal sealed class OperationStore : IManagementIdentityOperationRepository
{
    public Dictionary<(ManagementIdentityOperationScope, Guid), ManagementIdentityOperationEntity> Rows { get; } = [];
    public bool FailCreate { get; set; }
    public bool FailFinish { get; set; }
    public bool FailAcknowledge { get; set; }
    public Task<ManagementIdentityOperationEntity?> GetAsync(ManagementIdentityOperationScope scope, Guid id, CancellationToken ct = default)
    { lock (Rows) return Task.FromResult(Rows.GetValueOrDefault((scope, id))); }
    public Task<bool> TryCreateAsync(ManagementIdentityOperationEntity row, CancellationToken ct = default)
    {
        if (FailCreate) throw new Exception("provider-secret-canary");
        lock (Rows) {
            var key = (new ManagementIdentityOperationScope(row.IdentityRealmId, row.OrganizationId, row.ApplicationId), row.OperationId);
            if (Rows.ContainsKey(key)) return Task.FromResult(false);
            Rows.Add(key, row with { Version = Guid.NewGuid().ToString() }); return Task.FromResult(true);
        }
    }
    public Task<ManagementIdentityOperationEntity> ReplaceAsync(ManagementIdentityOperationEntity row, string version, CancellationToken ct = default)
    {
        if (FailFinish && row.Status is "succeeded" or "uncertain" || FailAcknowledge && !row.IntentPending && row.Status == "pending") throw new Exception("provider-secret-canary");
        lock (Rows) {
            var key = (new ManagementIdentityOperationScope(row.IdentityRealmId, row.OrganizationId, row.ApplicationId), row.OperationId);
            if (!Rows.TryGetValue(key, out var old) || old.Version != version) throw new ManagementIdentityOperationConflictException();
            Rows[key] = row = row with { Version = Guid.NewGuid().ToString() }; return Task.FromResult(row);
        }
    }
}
internal sealed class OperationHandler : IManagementIdentityMutationHandler
{
    public HashSet<Guid> Applied { get; } = [];
    public int Calls { get; private set; }
    public bool FailAfterWrite { get; set; }
    public bool Unknown { get; set; }
    public Func<CancellationToken, Task>? During { get; set; }
    public Task<ManagementIdentityReconciliation> ReconcileAsync(ManagementIdentityMutationContext context, CancellationToken ct)
    { lock (Applied) return Task.FromResult(Unknown ? ManagementIdentityReconciliation.Unknown : Applied.Contains(context.ResourceId)
        ? ManagementIdentityReconciliation.Applied : ManagementIdentityReconciliation.Absent); }
    public async Task ApplyAsync(ManagementIdentityMutationContext context, CancellationToken ct)
    {
        if (During is not null) await During(ct);
        lock (Applied) { Assert.IsTrue(Applied.Add(context.ResourceId), "Downstream conditional create must happen once."); Calls++; }
        if (FailAfterWrite) throw new Exception("lost-reply-secret-canary");
    }
}
