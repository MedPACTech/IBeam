using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Tenants;

public sealed class ManagementTenantDirectoryService(IManagementCustomerTenantReadRepository repository,
    IManagementCustomerIdentityTargetAccessService access, IOptions<ManagementTenantDirectoryOptions> options,
    IOptions<ManagementCustomerIdentityTargetOptions> realm, TimeProvider clock, IServiceOperationExecutor operations)
    : IManagementTenantDirectoryService, IManagementTenantDirectoryQuery
{
    private sealed record Cursor(string Continuation, DateTimeOffset ExpiresUtc)
    {
        public override string ToString() => "Customer directory cursor (redacted)";
    }
    public Task<ManagementCustomerDirectoryPage<ManagementCustomerTenantDto>> ListAsync(ManagementCustomerDirectoryQuery query, CancellationToken ct = default)
        => Run(async token =>
        {
            var actor = await access.RequireAsync(ManagementCustomerIdentityOperations.TenantsRead, token);
            if (query is null || query.PageSize is < 1 or > 100 || query.NamePrefix?.Length > 100 || query.NamePrefix?.Any(char.IsControl) == true ||
                query.Status is not (null or "Active" or "Disabled")) throw new ManagementCustomerIdentityInvalidRequestException();
            var prefix = query.NamePrefix?.Trim().ToUpperInvariant();
            if (prefix == "") prefix = null;
            var context = JsonSerializer.Serialize(new { actor.ApplicationKey, actor.Environment, actor.IdentityRealmId, actor.OrganizationId,
                actor.UserId, actor.CredentialId, repository.SourceId, query.PageSize, query.Status, prefix });
            var codec = new DiagnosticTokenCodec(Convert.FromBase64String(options.Value.ProtectionKey));
            var saved = query.ContinuationToken is null ? null : codec.Unprotect<Cursor>("customer-tenants", context, query.ContinuationToken);
            var expiry = saved?.ExpiresUtc ?? clock.GetUtcNow().AddMinutes(options.Value.TokenLifetimeMinutes);
            if (expiry <= clock.GetUtcNow() || saved?.Continuation.Length > 12000) throw new ManagementCustomerIdentityInvalidRequestException();
            var page = await repository.ReadPageAsync(query.PageSize, saved?.Continuation, token);
            if (page.Items.Count > query.PageSize || page.ContinuationToken?.Length > 12000) throw new ManagementCustomerIdentityUnavailableException();
            var items = page.Items.Select(Project).Where(x => x is not null).Cast<ManagementCustomerTenantDto>()
                .Where(x => (query.Status is null || x.Status == query.Status) &&
                    (prefix is null || x.Name.Trim().ToUpperInvariant().StartsWith(prefix, StringComparison.Ordinal))).ToArray();
            await access.RequireAsync(ManagementCustomerIdentityOperations.TenantsRead, token);
            string? continuation;
            try { continuation = page.ContinuationToken is null ? null : codec.Protect("customer-tenants", context, new Cursor(page.ContinuationToken, expiry)); }
            catch (DiagnosticTokenException) { throw new ManagementCustomerIdentityUnavailableException(); }
            return new ManagementCustomerDirectoryPage<ManagementCustomerTenantDto>(items, query.PageSize, continuation);
        }, null, ct);

    public Task<ManagementCustomerTenantDto?> FindAsync(Guid tenantId, CancellationToken ct = default)
        => Run<ManagementCustomerTenantDto?>(async token =>
        {
            await access.RequireAsync(ManagementCustomerIdentityOperations.TenantsRead, token);
            if (tenantId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
            var row = await repository.FindAsync(tenantId, token);
            var result = row is null ? null : Project(row);
            if (result is null || result.TenantId != tenantId) throw new ManagementCustomerIdentityNotFoundException();
            await access.RequireAsync(ManagementCustomerIdentityOperations.TenantsRead, token);
            return result;
        }, tenantId, ct);

    private static ManagementCustomerTenantDto? Project(ManagementCustomerTenantReadEntity row)
    {
        var status = string.IsNullOrWhiteSpace(row.Status) ? "Active" : row.Status;
        if (!Guid.TryParseExact(row.RowKey, "D", out var tenantId) || tenantId == Guid.Empty ||
            row.Name is not { Length: > 0 and <= 512 } || row.Name.Any(char.IsControl) || string.IsNullOrWhiteSpace(row.Name) ||
            status is not ("Active" or "Disabled") || row.Version is not { Length: > 0 and <= 256 }) return null;
        return new(tenantId, row.Name, status, row.CreatedUtc, row.UpdatedUtc, row.Version);
    }
    private Task<T> Run<T>(Func<CancellationToken, Task<T>> action, Guid? entity, CancellationToken ct)
        => operations.ExecuteAsync(this, async token =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try
            {
                if (!options.Value.IsValid() || !realm.Value.DedicatedApplicationRealm || realm.Value.IdentityRealmId == Guid.Empty)
                    throw new ManagementAccessDeniedException();
                return await action(timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (DiagnosticTokenException) { throw new ManagementCustomerIdentityInvalidRequestException(); }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or ManagementCustomerIdentityNotFoundException))
            { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = ManagementCustomerIdentityOperations.TenantsRead,
            AuditAction = ManagementCustomerIdentityOperations.TenantsRead, EntityName = "ManagementCustomerTenant", EntityId = entity,
            PermissionEnabled = false, AuditEnabled = true }, ct);
}
