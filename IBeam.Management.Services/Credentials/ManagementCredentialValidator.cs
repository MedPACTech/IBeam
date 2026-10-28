using IBeam.Management.Operations;
using IBeam.Management.Entities;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services.Credentials;

public sealed class ManagementCredentialValidator(
    IManagementApiCredentialRepository repository,
    ManagementCredentialSecretCodec secrets,
    IOptions<ManagementDiagnosticsOptions> deployment,
    IServiceOperationExecutor operations) : IManagementCredentialValidator
{
    private readonly ManagementOperationRegistry _registry = ManagementOperationRegistry.BuiltIn;
    public ManagementCredentialValidator(IManagementApiCredentialRepository repository,
    ManagementCredentialSecretCodec secrets,
    IOptions<ManagementDiagnosticsOptions> deployment,
    IServiceOperationExecutor operations, ManagementOperationRegistry registry)
        : this(repository, secrets, deployment, operations) => _registry = registry;

    [IBeamOperation("management.credentials.authenticate", Permission = false)]
    public Task<ManagementCredentialMetadata?> ValidateAsync(string apiKey, CancellationToken ct = default)
        => operations.ExecuteAsync<ManagementCredentialMetadata?>(this, async token =>
        {
            token.ThrowIfCancellationRequested();
            if (!secrets.TryReadId(apiKey, out var id)) return null;
            var configured = deployment.Value;
            var row = await repository.GetAsync(configured.ApplicationKey, configured.Environment, id, token);
            if (row is null || row.CredentialId != id || row.ApplicationKey != configured.ApplicationKey ||
                row.Environment != configured.Environment || row.Status != "Active" || row.RevokedUtc.HasValue ||
                row.ExpiresUtc <= DateTimeOffset.UtcNow || !_registry.ValidCapabilities(row.Capabilities) ||
                !secrets.Verify(apiKey, row.SecretHash)) return null;
            return Metadata(row);
        }, new ServiceOperationExecutionOptions { OperationName = "management.credentials.authenticate",
            PermissionEnabled = false, AuditEnabled = false }, ct);

    public static ManagementCredentialMetadata Metadata(ManagementApiCredentialEntity row)
        => new(row.CredentialId, row.ApplicationKey, row.Environment,
            Array.AsReadOnly(row.Capabilities.ToArray()), row.Status, row.ExpiresUtc) { Version = row.Version };
}
