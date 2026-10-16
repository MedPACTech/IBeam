using System.Text.RegularExpressions;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Secrets.AzureKeyVault;

/// <summary>Dedicated host-owned vault client; versioned references cannot select another vault or application.</summary>
public sealed class AzureKeyVaultManagementSecretStore(SecretClient client) : IManagementSecretStore
{
    public bool SupportsWrites => true;
    private static string Prefix(ManagementSecretScope scope)
    {
        if (scope.OrganizationId == Guid.Empty || scope.ApplicationId == Guid.Empty) throw new ManagementSecretStoreUnavailableException();
        return "ibmgmt-" + scope.OrganizationId.ToString("N") + "-" + scope.ApplicationId.ToString("N") + "-";
    }
    public async Task<string> CreateAsync(ManagementSecretScope scope, ManagementSecretValue secret, CancellationToken ct = default)
    {
        try
        {
            var name = Prefix(scope) + Guid.NewGuid().ToString("N");
            var candidate = new KeyVaultSecret(name, secret.Value);
            candidate.Properties.Tags["organization"] = scope.OrganizationId.ToString("N");
            candidate.Properties.Tags["application"] = scope.ApplicationId.ToString("N");
            var saved = (await client.SetSecretAsync(candidate, ct)).Value;
            if (saved.Name != name || !Regex.IsMatch(saved.Properties.Version ?? "", "^[A-Za-z0-9]{1,64}$"))
                throw new ManagementSecretStoreUnavailableException();
            return "kv:" + name + "/" + saved.Properties.Version;
        }
        catch (OperationCanceledException) { throw new OperationCanceledException("Management secret storage was cancelled.", ct); }
        catch { throw new ManagementSecretStoreUnavailableException(); }
    }
    public async Task<ManagementSecretValue> ReadAsync(ManagementSecretScope scope, string reference, CancellationToken ct = default)
    {
        try
        {
            var prefix = "kv:" + Prefix(scope);
            if (reference is null || !reference.StartsWith(prefix, StringComparison.Ordinal)) throw new ManagementSecretStoreUnavailableException();
            var parts = reference[3..].Split('/');
            if (parts.Length != 2 || !Regex.IsMatch(parts[0][Prefix(scope).Length..], "^[a-f0-9]{32}$") ||
                !Regex.IsMatch(parts[1], "^[A-Za-z0-9]{1,64}$")) throw new ManagementSecretStoreUnavailableException();
            var saved = (await client.GetSecretAsync(parts[0], parts[1], ct)).Value;
            if (saved.Name != parts[0] || saved.Properties.Version != parts[1] || saved.Properties.Enabled == false ||
                !saved.Properties.Tags.TryGetValue("organization", out var org) || org != scope.OrganizationId.ToString("N") ||
                !saved.Properties.Tags.TryGetValue("application", out var app) || app != scope.ApplicationId.ToString("N") ||
                string.IsNullOrWhiteSpace(saved.Value)) throw new ManagementSecretStoreUnavailableException();
            return new(saved.Value);
        }
        catch (OperationCanceledException) { throw new OperationCanceledException("Management secret storage was cancelled.", ct); }
        catch { throw new ManagementSecretStoreUnavailableException(); }
    }
}
public static class ManagementKeyVaultExtensions
{
    /// <summary>The dedicated client owns vault URI, managed identity and diagnostic redaction. No global client is replaced.</summary>
    public static IServiceCollection AddIBeamManagementKeyVaultSecrets(this IServiceCollection services, SecretClient managementVault)
    {
        ArgumentNullException.ThrowIfNull(managementVault);
        services.TryAddSingleton<IManagementSecretStore>(_ => new AzureKeyVaultManagementSecretStore(managementVault));
        return services;
    }
}
