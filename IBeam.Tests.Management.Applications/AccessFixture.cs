using System.Security.Claims;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Tests.Management.Applications;

internal sealed class AccessFixture : IDisposable, IManagementPrincipalProvider, IManagementSupportUserStateProvider, IAuditTrailSink
{
    public Guid Org { get; } = Guid.NewGuid();
    public Guid Admin { get; } = Guid.NewGuid();
    public Guid Reader { get; } = Guid.NewGuid();
    public Guid Actor { get; set; }
    public bool CanAdminister { get; set; } = true;
    public bool DelegateErrors { get; set; } = true;
    public bool DelegateLogs { get; set; } = true;
    public List<(Guid ApplicationId, string Operation)> CustomerDelegations { get; } = [];
    public MemoryApplications Apps { get; } = new();
    public MemoryAccess Access { get; } = new();
    public Dictionary<(Guid, Guid), ManagementSupportUserState> Users { get; } = [];
    public List<ServiceAuditTransaction> Audit { get; } = [];
    public ServiceProvider Provider { get; }
    public IManagementApplicationAccessService Service => Provider.GetRequiredService<IManagementApplicationAccessService>();
    public AccessFixture()
    {
        Actor = Admin;
        Users[(Org, Admin)] = new(Org, Admin, true, true); Users[(Org, Reader)] = new(Org, Reader, true, false);
        var services = new ServiceCollection();
        services.AddIBeamManagementApplicationAccess();
        services.AddSingleton<IManagementPrincipalProvider>(this);
        services.AddSingleton<IManagementSupportUserStateProvider>(this);
        services.AddSingleton<IManagementApplicationRepository>(Apps);
        services.AddSingleton<IManagementApplicationAccessRepository>(Access);
        services.AddSingleton<IAuditTrailSink>(this);
        services.Configure<ServiceAuditOptions>(x => x.Enabled = true);
        Provider = services.BuildServiceProvider();
    }
    public async Task<Guid> CreateApp(string key = "qurvia")
    {
        var result = await Apps.CreateAsync(new() { OrganizationId = Org, ApplicationId = Guid.NewGuid(), ApplicationKey = key,
            Environment = "Production", Name = key, IsEnabled = true });
        return result.ApplicationId;
    }
    public ClaimsPrincipal GetPrincipal()
    {
        var claims = new List<Claim> { new("sub", Actor.ToString()), new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human),
            new(ManagementClaimTypes.OrganizationId, Org.ToString()), new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead) };
        if (CanAdminister) claims.Add(new(ManagementClaimTypes.Permission, ManagementSupportOperations.AccessAdmin));
        if (DelegateErrors) claims.Add(new(ManagementClaimTypes.Permission, ManagementAccessDelegation.Errors));
        if (DelegateLogs) claims.Add(new(ManagementClaimTypes.Permission, ManagementAccessDelegation.Logs));
        claims.AddRange(CustomerDelegations.Select(x => new Claim(ManagementClaimTypes.Permission,
            IBeam.Management.CustomerIdentity.ManagementCustomerIdentityPermissionPolicy.DelegationFor(x.Operation, x.ApplicationId))));
        return new(new ClaimsIdentity(claims, "Support"));
    }
    public Task<ManagementSupportUserState?> GetAsync(Guid org, Guid user, CancellationToken ct = default) => Task.FromResult(Users.GetValueOrDefault((org, user)));
    public Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default) { Audit.Add(transaction); return Task.CompletedTask; }
    public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default) => Task.CompletedTask;
    public void Dispose() => Provider.Dispose();
}
internal sealed class MemoryAccess : IManagementApplicationAccessRepository
{
    public Dictionary<(Guid, Guid, Guid), ManagementApplicationAccessEntity> Rows { get; } = [];
    public bool Outage { get; set; }
    public Task<ManagementApplicationAccessEntity?> GetAsync(Guid org, Guid app, Guid user, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Outage) throw new InvalidOperationException("provider-secret-canary");
        return Task.FromResult(Rows.GetValueOrDefault((org, app, user)));
    }
    public Task<ManagementApplicationAccessPage> ListAsync(Guid org, Guid app, Guid? after, int size, CancellationToken ct = default)
    {
        var rows = Rows.Values.Where(x => x.OrganizationId == org && x.ApplicationId == app &&
            (!after.HasValue || string.CompareOrdinal(x.UserId.ToString("N"), after.Value.ToString("N")) > 0)).OrderBy(x => x.UserId.ToString("N")).ToArray();
        return Task.FromResult(new ManagementApplicationAccessPage(rows.Take(size).ToArray(), rows.Length > size ? rows[size - 1].UserId : null));
    }
    public Task<ManagementApplicationAccessEntity> CreateAsync(ManagementApplicationAccessEntity row, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Rows.ContainsKey((row.OrganizationId, row.ApplicationId, row.UserId))) throw new ManagementApplicationConflictException();
        row = row with { Version = Guid.NewGuid().ToString() }; Rows.Add((row.OrganizationId, row.ApplicationId, row.UserId), row); return Task.FromResult(row);
    }
    public Task<ManagementApplicationAccessEntity> ReplaceAsync(ManagementApplicationAccessEntity row, string version, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = (row.OrganizationId, row.ApplicationId, row.UserId);
        if (!Rows.TryGetValue(key, out var old) || version != old.Version) throw new ManagementApplicationConflictException();
        row = row with { Version = Guid.NewGuid().ToString() }; Rows[key] = row; return Task.FromResult(row);
    }
}
