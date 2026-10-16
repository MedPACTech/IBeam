using IBeam.Management.Entities;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services.Credentials;

public sealed class ManagementCredentialProvisioningService(
    IManagementApiCredentialRepository repository,
    IManagementCredentialOperatorProvider operators,
    ManagementCredentialSecretCodec secrets,
    IOptions<ManagementDiagnosticsOptions> deployment,
    IOptions<ManagementCredentialProvisioningOptions> provisioning,
    IServiceOperationExecutor operations) : IManagementCredentialProvisioningService
{
    [IBeamOperation("management.credentials.issue", Permission = false)]
    public Task<ManagementIssuedCredential> IssueAsync(IReadOnlyList<string> capabilities, DateTimeOffset expiresUtc, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            var actor = await RequireOperatorAsync(token);
            var now = DateTimeOffset.UtcNow;
            var requested = capabilities?.ToArray();
            if (!ManagementCredentialPolicy.ValidCapabilities(requested) || expiresUtc <= now ||
                expiresUtc > now.AddDays(provisioning.Value.MaximumLifetimeDays))
                throw new ArgumentException("Explicit supported capabilities and a bounded future expiry are required.");
            var id = Guid.NewGuid();
            var key = secrets.Create(id);
            var configured = deployment.Value;
            var row = await repository.CreateAsync(new ManagementApiCredentialEntity
            {
                CredentialId = id, ApplicationKey = configured.ApplicationKey, Environment = configured.Environment,
                SecretHash = secrets.Hash(key), Capabilities = Array.AsReadOnly(requested!),
                CreatedUtc = now, ExpiresUtc = expiresUtc, CreatedByOperatorId = actor
            }, token);
            return Issued(row, key);
        }, Audit("management.credentials.issue"), ct);

    [IBeamOperation("management.credentials.rotate", Permission = false)]
    public Task<ManagementIssuedCredential> RotateAsync(Guid credentialId, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            var actor = await RequireOperatorAsync(token);
            var row = await RequireCredentialAsync(credentialId, token);
            if (row.Status != "Active" || row.RevokedUtc.HasValue || row.ExpiresUtc <= DateTimeOffset.UtcNow)
                throw new ManagementAccessDeniedException();
            var key = secrets.Create(credentialId);
            var updated = await repository.ReplaceAsync(row with
            {
                SecretHash = secrets.Hash(key), RotatedUtc = DateTimeOffset.UtcNow, RotatedByOperatorId = actor
            }, row.Version, token);
            return Issued(updated, key);
        }, Audit("management.credentials.rotate", credentialId), ct);

    [IBeamOperation("management.credentials.revoke", Permission = false)]
    public Task RevokeAsync(Guid credentialId, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            var actor = await RequireOperatorAsync(token);
            var row = await RequireCredentialAsync(credentialId, token);
            if (row.Status == "Revoked" && row.RevokedUtc.HasValue) return;
            await repository.ReplaceAsync(row with
            {
                Status = "Revoked", RevokedUtc = DateTimeOffset.UtcNow, RevokedByOperatorId = actor
            }, row.Version, token);
        }, Audit("management.credentials.revoke", credentialId), ct);

    private async Task<Guid> RequireOperatorAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!provisioning.Value.Enabled) throw new ManagementAccessDeniedException();
        var actor = await operators.RequireAuthorizedOperatorAsync(ct);
        if (actor == Guid.Empty) throw new ManagementAccessDeniedException();
        return actor;
    }

    private async Task<ManagementApiCredentialEntity> RequireCredentialAsync(Guid id, CancellationToken ct)
    {
        var configured = deployment.Value;
        var row = await repository.GetAsync(configured.ApplicationKey, configured.Environment, id, ct);
        if (row is null || row.CredentialId != id || row.ApplicationKey != configured.ApplicationKey || row.Environment != configured.Environment)
            throw new ManagementAccessDeniedException();
        return row;
    }

    private static ManagementIssuedCredential Issued(ManagementApiCredentialEntity row, string key)
        => new() { ApiKey = key, Credential = ManagementCredentialValidator.Metadata(row) };

    // No entity, request, secret/hash, or return-value snapshots go into the shared audit pipeline.
    private static ServiceOperationExecutionOptions Audit(string action, Guid? id = null)
        => new() { OperationName = action, AuditAction = action, EntityId = id,
            EntityName = nameof(ManagementApiCredentialEntity), PermissionEnabled = false, AuditEnabled = true };
}
