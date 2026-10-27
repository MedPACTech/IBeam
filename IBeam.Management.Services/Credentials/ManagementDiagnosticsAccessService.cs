using IBeam.Management.Operations;
using System.Security.Claims;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services.Credentials;

/// <summary>Future diagnostic readers must call this before every repository query.</summary>
public interface IManagementDiagnosticsAccessService
{
    Task<ManagementCredentialMetadata> RequireAsync(string operation, CancellationToken ct = default);
}

public sealed class ManagementDiagnosticsAccessService(
    IManagementPrincipalProvider principals,
    IManagementApiCredentialRepository repository,
    IOptions<ManagementDiagnosticsOptions> deployment,
    IServiceOperationExecutor operations) : IManagementDiagnosticsAccessService
{
    private readonly ManagementOperationRegistry _registry = ManagementOperationRegistry.BuiltIn;
    public ManagementDiagnosticsAccessService(IManagementPrincipalProvider principals,
    IManagementApiCredentialRepository repository,
    IOptions<ManagementDiagnosticsOptions> deployment,
    IServiceOperationExecutor operations, ManagementOperationRegistry registry)
        : this(principals, repository, deployment, operations) => _registry = registry;

    public Task<ManagementCredentialMetadata> RequireAsync(string operation, CancellationToken ct = default)
    {
        if (operation is not (ManagementOperations.DiagnosticsInfo or ManagementOperations.ErrorsRead or ManagementOperations.LogsRead))
            throw new ManagementAccessDeniedException();
        var principal = principals.GetPrincipal();
        var parsed = Guid.TryParse(principal.FindFirst(ManagementClaimTypes.CredentialId)?.Value, out var candidate);
        return operations.ExecuteAsync(this, async token =>
        {
            token.ThrowIfCancellationRequested();
            var identity = ManagementPrincipalGuard.RequireIdentity(principal, ManagementSubjectTypes.Credential, operation);
            var id = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.CredentialId);
            var configured = deployment.Value;
            if (ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.ApplicationKey) != configured.ApplicationKey ||
                ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.Environment) != configured.Environment)
                throw new ManagementAccessDeniedException();
            var row = await repository.GetAsync(configured.ApplicationKey, configured.Environment, id, token);
            if (row is null || row.CredentialId != id || row.ApplicationKey != configured.ApplicationKey ||
                row.Environment != configured.Environment || row.Status != "Active" || row.RevokedUtc.HasValue ||
                row.ExpiresUtc <= DateTimeOffset.UtcNow || row.Version != ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.CredentialVersion) ||
                !_registry.ValidCapabilities(row.Capabilities) ||
                !row.Capabilities.Contains(operation, StringComparer.Ordinal))
                throw new ManagementAccessDeniedException();
            return ManagementCredentialValidator.Metadata(row);
        }, new ServiceOperationExecutionOptions
        {
            OperationName = operation, AuditAction = operation, EntityName = "ManagementApiCredential",
            EntityId = parsed ? candidate : null, PermissionEnabled = false, AuditEnabled = true
        }, ct);
    }
}
