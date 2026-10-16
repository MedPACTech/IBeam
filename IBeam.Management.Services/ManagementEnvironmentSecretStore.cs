using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services;

public sealed class ManagementEnvironmentSecretOptions
{
    /// <summary>Trusted host configuration, never portal input.</summary>
    public List<ManagementEnvironmentSecretBinding> Bindings { get; set; } = [];
}
public sealed record ManagementEnvironmentSecretBinding(Guid OrganizationId, Guid ApplicationId, string Reference, string VariableName);
public sealed class ManagementEnvironmentSecretStore(IOptions<ManagementEnvironmentSecretOptions> options) : IManagementSecretStore
{
    public bool SupportsWrites => false;
    public Task<string> CreateAsync(ManagementSecretScope scope, ManagementSecretValue secret, CancellationToken ct = default)
        => throw new ManagementConnectionInvalidException();
    public Task<ManagementSecretValue> ReadAsync(ManagementSecretScope scope, string reference, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var bindings = options.Value.Bindings.Where(x => x.OrganizationId == scope.OrganizationId && x.ApplicationId == scope.ApplicationId && x.Reference == reference).ToArray();
        if (bindings.Length != 1) throw new ManagementSecretStoreUnavailableException();
        var value = Environment.GetEnvironmentVariable(bindings[0].VariableName);
        if (string.IsNullOrWhiteSpace(value)) throw new ManagementSecretStoreUnavailableException();
        return Task.FromResult(new ManagementSecretValue(value));
    }
}
public static class ManagementEnvironmentSecretExtensions
{
    public static IServiceCollection AddIBeamManagementEnvironmentSecrets(this IServiceCollection services, Action<ManagementEnvironmentSecretOptions> configure)
    {
        services.Configure(configure);
        services.TryAddSingleton<IManagementSecretStore, ManagementEnvironmentSecretStore>();
        return services;
    }
}
