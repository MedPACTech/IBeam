using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tenants;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Identity.Target.Services.Tenants;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

internal sealed class TenantAdministrationFixture
{
    public OperationFixture Operations { get; } = new();
    public TenantMetadataStore Metadata { get; } = new();
    public ConditionalTenantStore Store { get; } = new();
    public TenantAdministrationHooks Hooks { get; } = new();
    public IIdentityTenantAdministrationService Identity { get; }
    public IManagementTenantAdministrationService Service { get; }
    public TenantAdministrationFixture(bool conditional = true)
    {
        IIdentityTenantStore store = conditional ? Store : new LegacyTenantStore(Store);
        var publisher = Mock.Of<IAuthEventPublisher>(); var lifecycle = Mock.Of<IAuthLifecycleHook>(); var options = Options.Create(new AuthEventOptions { StrictPublishFailures = true });
        var tenants = new IdentityTenantService(store, Hooks, publisher, lifecycle, options, NullLogger<IdentityTenantService>.Instance);
        Identity = new IdentityTenantAdministrationService(tenants, store, Hooks, lifecycle, publisher, options,
            NullLogger<IdentityTenantAdministrationService>.Instance, new ServiceOperationExecutor());
        Service = new ManagementTenantAdministrationService(Identity, Metadata, Operations.Service, Operations, Options.Create(Operations.Options), new ServiceOperationExecutor());
    }
    public void UpdateAuthority() => Operations.Actor = Operations.Actor with { Operation = ManagementCustomerIdentityOperations.TenantsUpdate };
}
internal sealed class TenantAdministrationHooks : ITenantExtensionCoordinator
{
    public int Created { get; private set; }
    public int Updated { get; private set; }
    public bool FailCreated { get; set; }
    public bool FailUpdated { get; set; }
    public TenantExtensionContext? Context { get; private set; }
    public IdentityTenant? Previous { get; private set; }
    public Task OnTenantCreatedAsync(IdentityTenant tenant, TenantExtensionContext context, CancellationToken ct = default)
    { Created++; Context = context; if (FailCreated) throw new Exception("CONSUMER-PROFILE-SECRET-CANARY"); return Task.CompletedTask; }
    public Task OnTenantUpdatedAsync(IdentityTenant tenant, IdentityTenant? previous, TenantExtensionContext context, CancellationToken ct = default)
    { Updated++; Context = context; Previous = previous; if (FailUpdated) throw new Exception("CONSUMER-PROFILE-SECRET-CANARY"); return Task.CompletedTask; }
    public Task EnsureExtensionAsync(IdentityTenant tenant, TenantExtensionContext context, CancellationToken ct = default) => Task.CompletedTask;
    public Task OnTenantActivatedAsync(IdentityTenant tenant, TenantExtensionContext context, CancellationToken ct = default) => Task.CompletedTask;
    public Task OnTenantDeactivatedAsync(IdentityTenant tenant, TenantExtensionContext context, CancellationToken ct = default) => Task.CompletedTask;
}
internal sealed class ConditionalTenantStore : IIdentityTenantStore, IIdentityTenantConditionalStore
{
    public Dictionary<Guid, VersionedIdentityTenant> Rows { get; } = [];
    public int Creates { get; private set; }
    public int Renames { get; private set; }
    public Task<IdentityTenant?> FindByIdAsync(Guid id, CancellationToken ct = default) { lock (Rows) return Task.FromResult(Rows.GetValueOrDefault(id)?.Tenant); }
    public Task<VersionedIdentityTenant?> FindVersionedAsync(Guid id, CancellationToken ct = default) { lock (Rows) return Task.FromResult(Rows.GetValueOrDefault(id)); }
    public Task<IdentityTenant> CreateAsync(IdentityTenant tenant, CancellationToken ct = default)
    {
        lock (Rows) {
            if (Rows.ContainsKey(tenant.TenantId)) throw new IdentityValidationException("Tenant already exists.");
            Creates++; Rows.Add(tenant.TenantId, new(tenant, Guid.NewGuid().ToString(), null)); return Task.FromResult(tenant);
        }
    }
    public Task<IdentityTenant> UpdateAsync(IdentityTenant tenant, CancellationToken ct = default)
    { lock (Rows) { Rows[tenant.TenantId] = new(tenant, Guid.NewGuid().ToString(), null); return Task.FromResult(tenant); } }
    public async Task<IdentityTenant> SetStatusAsync(Guid id, string status, CancellationToken ct = default)
        => await UpdateAsync((await FindByIdAsync(id, ct))! with { Status = status }, ct);
    public Task<VersionedIdentityTenant> RenameAsync(Guid id, string name, string expectedVersion, Guid operationId, CancellationToken ct = default)
    {
        lock (Rows) {
            if (!Rows.TryGetValue(id, out var old) || old.Version != expectedVersion || expectedVersion == "*") throw new IdentityConcurrencyException();
            Renames++; Rows[id] = new(old.Tenant with { Name = name, NormalizedName = IdentityTenant.NormalizeName(name), UpdatedAt = DateTimeOffset.UtcNow },
                Guid.NewGuid().ToString(), operationId); return Task.FromResult(Rows[id]);
        }
    }
}
internal sealed class LegacyTenantStore(ConditionalTenantStore inner) : IIdentityTenantStore
{
    public Task<IdentityTenant?> FindByIdAsync(Guid id, CancellationToken ct = default) => inner.FindByIdAsync(id, ct);
    public Task<IdentityTenant> CreateAsync(IdentityTenant tenant, CancellationToken ct = default) => inner.CreateAsync(tenant, ct);
    public Task<IdentityTenant> UpdateAsync(IdentityTenant tenant, CancellationToken ct = default) => inner.UpdateAsync(tenant, ct);
    public Task<IdentityTenant> SetStatusAsync(Guid id, string status, CancellationToken ct = default) => inner.SetStatusAsync(id, status, ct);
}
internal sealed class TenantMetadataStore : IManagementTenantAdministrationRepository
{
    public Dictionary<(ManagementTenantAdministrationScope, Guid), ManagementTenantAdministrationEntity> Rows { get; } = [];
    private static ManagementTenantAdministrationScope Scope(ManagementTenantAdministrationEntity row) => new(row.IdentityRealmId, row.ApplicationKey, row.Environment);
    public Task<ManagementTenantAdministrationEntity?> GetAsync(ManagementTenantAdministrationScope scope, Guid id, CancellationToken ct = default)
    { lock (Rows) return Task.FromResult(Rows.GetValueOrDefault((scope, id))); }
    public Task<bool> TryCreateAsync(ManagementTenantAdministrationEntity row, CancellationToken ct = default)
    {
        lock (Rows) {
            var scope = Scope(row);
            if (Rows.ContainsKey((scope, row.TenantId)) || row.ExternalReference is not null && Rows.Values.Any(x => Scope(x) == scope &&
                string.Equals(x.ExternalReference, row.ExternalReference, StringComparison.OrdinalIgnoreCase))) return Task.FromResult(false);
            Rows.Add((scope, row.TenantId), row with { Version = Guid.NewGuid().ToString() }); return Task.FromResult(true);
        }
    }
    public Task<ManagementTenantAdministrationEntity> ReplaceAsync(ManagementTenantAdministrationEntity row, string version, CancellationToken ct = default)
    {
        lock (Rows) {
            var key = (Scope(row), row.TenantId);
            if (!Rows.TryGetValue(key, out var old) || old.Version != version || version == "*") throw new ManagementIdentityOperationConflictException();
            row = row with { Version = Guid.NewGuid().ToString() }; Rows[key] = row; return Task.FromResult(row);
        }
    }
}
