using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Services;
using IBeam.Management.Services.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Tests.Management.Credentials;

internal sealed class CredentialFixture : IAsyncDisposable
{
    public MemoryCredentialRepository Repository { get; } = new();
    public Guid OperatorId { get; } = Guid.NewGuid();
    public ServiceProvider Provider { get; }
    public IManagementCredentialProvisioningService Provisioning => Provider.GetRequiredService<IManagementCredentialProvisioningService>();
    public IManagementCredentialValidator Validator => Provider.GetRequiredService<IManagementCredentialValidator>();

    public CredentialFixture(bool enabled = true, bool authorizedOperator = true, Action<IServiceCollection>? customize = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IBeam:Management:Diagnostics:ApplicationKey"] = "qurvia",
            ["IBeam:Management:Diagnostics:Environment"] = "Production",
            ["IBeam:Management:Credentials:Provisioning:Enabled"] = enabled.ToString()
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIBeamManagementCredentialProvisioning(configuration);
        services.AddSingleton<IManagementApiCredentialRepository>(Repository);
        if (authorizedOperator) services.Replace(ServiceDescriptor.Singleton<IManagementCredentialOperatorProvider>(new Operator(OperatorId)));
        customize?.Invoke(services);
        Provider = services.BuildServiceProvider();
    }

    public Task<ManagementIssuedCredential> IssueAsync(params string[] capabilities)
        => Provisioning.IssueAsync(capabilities.Length == 0 ? [ManagementOperations.DiagnosticsInfo] : capabilities, DateTimeOffset.UtcNow.AddDays(1));
    public ValueTask DisposeAsync() => Provider.DisposeAsync();

    private sealed class Operator(Guid id) : IManagementCredentialOperatorProvider
    {
        public Task<Guid> RequireAuthorizedOperatorAsync(CancellationToken ct = default) => Task.FromResult(id);
    }
}

internal sealed class MemoryCredentialRepository : IManagementApiCredentialRepository
{
    private readonly Dictionary<(string, string, Guid), ManagementApiCredentialEntity> _rows = [];
    private readonly object _gate = new();
    public int Reads { get; private set; }
    public bool SimulateReadOutage { get; set; }
    public Task<ManagementApiCredentialEntity?> GetAsync(string applicationKey, string environment, Guid credentialId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Reads++;
            if (SimulateReadOutage) throw new InvalidOperationException("Fixture storage unavailable.");
            return Task.FromResult(_rows.GetValueOrDefault((applicationKey, environment, credentialId)));
        }
    }
    public Task<ManagementApiCredentialEntity> CreateAsync(ManagementApiCredentialEntity credential, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var saved = credential with { Version = Guid.NewGuid().ToString("N") };
            _rows.Add((saved.ApplicationKey, saved.Environment, saved.CredentialId), saved);
            return Task.FromResult(saved);
        }
    }
    public Task<ManagementApiCredentialEntity> ReplaceAsync(ManagementApiCredentialEntity credential, string expectedVersion, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (credential.ApplicationKey, credential.Environment, credential.CredentialId);
            if (!_rows.TryGetValue(key, out var current) || current.Version != expectedVersion)
                throw new ManagementCredentialConflictException();
            var saved = credential with { Version = Guid.NewGuid().ToString("N") };
            _rows[key] = saved;
            return Task.FromResult(saved);
        }
    }
}
