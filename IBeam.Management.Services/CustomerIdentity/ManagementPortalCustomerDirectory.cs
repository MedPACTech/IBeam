using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed class ManagementPortalCustomerDirectory<T>(IManagementCustomerIdentityConnectionResolver connections,
    IManagementCustomerIdentityClient client, IOptions<ManagementPortalCustomerDirectoryOptions> options,
    TimeProvider clock, IServiceOperationExecutor operations) : IManagementPortalCustomerDirectory<T> where T : class
{
    private sealed record Cursor(string Continuation, DateTimeOffset ExpiresUtc)
    { public override string ToString() => "Portal customer cursor (redacted)"; }

    public Task<ManagementCustomerDirectoryPage<T>> ListAsync(Guid applicationId, Guid? tenantId, ManagementCustomerDirectoryQuery query, CancellationToken ct = default)
        => Run(applicationId, tenantId, "list", async token =>
        {
            var resource = ManagementCustomerDirectoryResource.For<T>(tenantId);
            var c = await connections.ResolveAsync(applicationId, resource.Operation, token);
            if (query is null || query.PageSize is < 1 or > 100 || query.NamePrefix?.Length > 100 || query.NamePrefix?.Any(char.IsControl) == true ||
                !Status(resource.Name, query.Status)) throw new ManagementCustomerIdentityInvalidRequestException();
            var prefix = query.NamePrefix?.Trim();
            if (prefix == "") prefix = null;
            var context = JsonSerializer.Serialize(new { c.OrganizationId, c.UserId, c.ApplicationId, c.CredentialId, c.IdentityRealmId,
                c.ApplicationKey, c.Environment, c.BaseUrl, c.Version, resource.Operation, tenantId, query.PageSize, query.Status, prefix });
            var codec = new DiagnosticTokenCodec(Convert.FromBase64String(options.Value.ProtectionKey));
            var saved = query.ContinuationToken is null ? null : codec.Unprotect<Cursor>("portal-customer-directory", context, query.ContinuationToken);
            var expiry = saved?.ExpiresUtc ?? clock.GetUtcNow().AddMinutes(options.Value.TokenLifetimeMinutes);
            if (expiry <= clock.GetUtcNow() || saved is not null && saved.Continuation is not { Length: > 0 and <= 7000 })
                throw new ManagementCustomerIdentityInvalidRequestException();
            var page = await client.ListAsync<T>(c, tenantId, new(query.PageSize, saved?.Continuation, query.Status, prefix), token);
            await connections.RecheckAsync(c, resource.Operation, token);
            if (page.PageSize != query.PageSize || page.Items.Count > query.PageSize || page.ContinuationToken?.Length > 7000)
                throw new ManagementCustomerIdentityResponseException();
            var next = page.ContinuationToken is null ? null : codec.Protect("portal-customer-directory", context, new Cursor(page.ContinuationToken, expiry));
            return new ManagementCustomerDirectoryPage<T>(page.Items, page.PageSize, next);
        }, ct);

    public Task<T> FindAsync(Guid applicationId, Guid? tenantId, Guid id, CancellationToken ct = default)
        => Run(applicationId, tenantId, "read", async token =>
        {
            var resource = ManagementCustomerDirectoryResource.For<T>(tenantId);
            var c = await connections.ResolveAsync(applicationId, resource.Operation, token);
            if (id == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
            var result = await client.FindAsync<T>(c, tenantId, id, token);
            await connections.RecheckAsync(c, resource.Operation, token);
            return result;
        }, ct);

    private Task<TResult> Run<TResult>(Guid app, Guid? tenant, string action, Func<CancellationToken, Task<TResult>> run, CancellationToken ct)
    {
        var resource = ManagementCustomerDirectoryResource.For<T>(tenant);
        var operation = "management.portal.customer-identity." + resource.Name + "." + action;
        return operations.ExecuteAsync(this, async token =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try
            {
                if (!options.Value.IsValid()) throw new ManagementCustomerIdentityUnavailableException();
                return await run(timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (DiagnosticTokenException) { throw new ManagementCustomerIdentityInvalidRequestException(); }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or
                ManagementCustomerIdentityNotFoundException or ManagementCustomerIdentityResponseException or ManagementCustomerIdentityRateLimitException or
                ManagementCustomerIdentityUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = operation, EntityName = "ManagementCustomerDirectory",
            EntityId = app, PermissionEnabled = false, AuditEnabled = true }, ct);
    }
    private static bool Status(string resource, string? value) => value is null || resource switch {
        "tenants" => value is "Active" or "Disabled", "users" => value is "active" or "disabled",
        "invites" => value is "pending" or "sent" or "expired" or "redeemed" or "revoked", _ => false };
}
