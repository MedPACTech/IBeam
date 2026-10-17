using System.Security.Claims;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Tests.Management.Applications;

internal sealed class ApplicationFixture : IDisposable, IManagementPrincipalProvider, IAuditTrailSink
{
    public Guid OrganizationId { get; set; } = Guid.NewGuid();
    public bool Admin { get; set; } = true;
    public bool Machine { get; set; }
    public MemoryApplications Repository { get; } = new();
    public List<ServiceAuditTransaction> Audit { get; } = [];
    public ServiceProvider Provider { get; }
    public IManagementApplicationService Service => Provider.GetRequiredService<IManagementApplicationService>();
    public ApplicationFixture()
    {
        var services = new ServiceCollection();
        services.AddIBeamManagementApplications();
        services.AddSingleton<IManagementApplicationRepository>(Repository);
        services.AddSingleton<IManagementPrincipalProvider>(this);
        services.AddSingleton<IAuditTrailSink>(this);
        services.Configure<ServiceAuditOptions>(x => x.Enabled = true);
        Provider = services.BuildServiceProvider();
    }
    public ClaimsPrincipal GetPrincipal()
    {
        var claims = new List<Claim> {
            new("sub", "operator"), new(ManagementClaimTypes.OrganizationId, OrganizationId.ToString()),
            new(ManagementClaimTypes.SubjectType, Machine ? ManagementSubjectTypes.Credential : ManagementSubjectTypes.Human),
            new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead) };
        if (Admin) claims.Add(new(ManagementClaimTypes.Permission, ManagementApplicationOperations.Admin));
        return new(new ClaimsIdentity(claims, "Support"));
    }
    public static CreateManagementApplication Request(string key = "qurvia") => new()
        { ApplicationKey = key, Environment = "Production", Name = "Qurvia", BaseUrl = "https://api.qurvia.app" };
    public void Dispose() => Provider.Dispose();
    public Task WriteTransactionAsync(ServiceAuditTransaction row, CancellationToken ct = default) { Audit.Add(row); return Task.CompletedTask; }
    public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default) => Task.CompletedTask;
}
internal sealed class MemoryApplications : IManagementApplicationRepository
{
    public Dictionary<(Guid, Guid), ManagementApplicationEntity> Rows { get; } = [];
    public int Reads { get; set; }
    public bool Outage { get; set; }
    public Task<ManagementApplicationEntity?> GetAsync(Guid org, Guid app, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Reads++;
        if (Outage) throw new Exception("unsafe provider message with secret");
        return Task.FromResult(Rows.GetValueOrDefault((org, app)));
    }
    public Task<ManagementApplicationPage> ListAsync(Guid org, Guid? after, int size, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Reads++;
        var rows = Rows.Values.Where(x => x.OrganizationId == org && (!after.HasValue ||
            string.CompareOrdinal(x.ApplicationId.ToString("N"), after.Value.ToString("N")) > 0)).OrderBy(x => x.ApplicationId.ToString("N")).ToArray();
        return Task.FromResult(new ManagementApplicationPage(rows.Take(size).ToArray(), rows.Length > size ? rows[size - 1].ApplicationId : null));
    }
    public Task<ManagementApplicationEntity> CreateAsync(ManagementApplicationEntity row, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Rows.Values.Any(x => x.OrganizationId == row.OrganizationId &&
            x.ApplicationKey.Equals(row.ApplicationKey, StringComparison.OrdinalIgnoreCase) &&
            x.Environment.Equals(row.Environment, StringComparison.OrdinalIgnoreCase))) throw new ManagementApplicationConflictException();
        row = row with { Version = Guid.NewGuid().ToString() };
        Rows.Add((row.OrganizationId, row.ApplicationId), row); return Task.FromResult(row);
    }
    public Task<ManagementApplicationEntity> ReplaceAsync(ManagementApplicationEntity row, string version, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Rows.TryGetValue((row.OrganizationId, row.ApplicationId), out var existing) || existing.Version != version)
            throw new ManagementApplicationConflictException();
        row = row with { Version = Guid.NewGuid().ToString() };
        Rows[(row.OrganizationId, row.ApplicationId)] = row; return Task.FromResult(row);
    }
}
