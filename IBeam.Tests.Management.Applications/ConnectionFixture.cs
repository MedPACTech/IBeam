using Microsoft.Extensions.DependencyInjection;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.Applications;

internal sealed class ConnectionFixture : IDisposable
{
    public const string Key = "fixture-management-key-123456789";
    public ApplicationFixture Apps { get; } = new();
    public MemoryApplicationCredentials Credentials { get; } = new();
    public MemorySecrets Secrets { get; } = new();
    public FakeProbe Probe { get; } = new();
    public ManagementApplicationConnectionService Service { get; }
    public ManagementEnvironmentSecretOptions Bindings { get; } = new();
    public ConnectionFixture(IManagementSecretStore? store = null)
    {
        Service = new(Apps.Provider.GetRequiredService<ManagementApplicationService>(), Credentials, store ?? Secrets, Probe,
            Apps, Apps.Provider.GetRequiredService<IServiceOperationExecutor>(),
            Options.Create(new ManagementConnectionOptions { ApprovedOrigins = ["https://api.qurvia.app"], TimeoutSeconds = 1 }), Options.Create(Bindings));
    }
    public async Task<ManagementApplicationDto> CreateAsync() => await Apps.Service.CreateAsync(ApplicationFixture.Request());
    public void Dispose() => Apps.Dispose();
}
internal sealed class MemoryApplicationCredentials : IManagementApplicationCredentialRepository
{
    public List<ManagementApplicationCredentialEntity> Rows { get; } = [];
    public Task CreateAsync(ManagementApplicationCredentialEntity row, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); Rows.Add(row); return Task.CompletedTask; }
    public Task<ManagementApplicationCredentialEntity?> GetAsync(Guid org, Guid app, Guid id, CancellationToken ct = default)
        => Task.FromResult(Rows.SingleOrDefault(x => x.OrganizationId == org && x.ApplicationId == app && x.CredentialId == id));
}
internal sealed class MemorySecrets : IManagementSecretStore
{
    public bool SupportsWrites { get; set; } = true;
    public int Reads { get; private set; }
    public Dictionary<string, (ManagementSecretScope Scope, string Value)> Rows { get; } = [];
    public Task<string> CreateAsync(ManagementSecretScope scope, ManagementSecretValue value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var reference = "fixture:" + Guid.NewGuid();
        Rows.Add(reference, (scope, value.Value));
        return Task.FromResult(reference);
    }
    public Task<ManagementSecretValue> ReadAsync(ManagementSecretScope scope, string reference, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Reads++;
        var row = Rows[reference];
        if (row.Scope != scope) throw new ManagementSecretStoreUnavailableException();
        return Task.FromResult(new ManagementSecretValue(row.Value));
    }
}
internal sealed class FakeProbe : IManagementTargetConnectionProbe
{
    public ManagementDiagnosticsInfo Info { get; set; } = new("qurvia", "Production", 1, [ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead]);
    public Func<CancellationToken, Task>? During { get; set; }
    public Exception? Failure { get; set; }
    public int Calls { get; set; }
    public async Task<ManagementDiagnosticsInfo> ProbeAsync(string origin, ManagementSecretValue key, CancellationToken ct = default)
    {
        Calls++;
        if (During is not null) await During(ct);
        if (Failure is not null) throw Failure;
        return Info;
    }
}
