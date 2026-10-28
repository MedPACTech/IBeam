using Azure;
using Azure.Core;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

/// <summary>One immutable entity-family/table descriptor per instance. Selectors are compiled provider code, never portal input.</summary>
public sealed class AzureTableIdentityTenantPurgeResourceStore(TableClient table, string key, IOptions<AzureTableIdentityOptions> options) : IIdentityTenantPurgeResourceStore
{
    public int ProtocolVersion => 1;
    public string Key => key;
    public bool ProtectsAllWriters {
        get {
            var o = options.Value;
            var names = new[] { o.TenantsTableName, o.TenantUsersTableName, o.UserTenantsTableName, o.TenantRolesTableName, o.TenantInvitesTableName,
                o.ApiCredentialsTableName, o.AgentUsersTableName, o.AgentUserCredentialsTableName, o.OAuthConsentsTableName, o.AccessCatalogOverridesTableName };
            return o.RequireTenantPurgeFencing && names.All(x => !string.IsNullOrWhiteSpace(x)) && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length && IdentityTenantPurgeInventory.Keys.Contains(key, StringComparer.Ordinal);
        }
    }
    private static readonly string[] Columns = ["PartitionKey", "RowKey", "TenantId", "TokenHash"];
    public async Task<bool> HasPurgeMarkerAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default)
    {
        RequireSupport(); if (scope.TenantId == Guid.Empty || scope.JobId == Guid.Empty) return false;
        var root = await table.GetEntityIfExistsAsync<TableEntity>(IdentityTenantPurgePartition.RootPartition(scope.TenantId), IdentityTenantPurgePartition.RootKey, cancellationToken: ct);
        return root.HasValue && root.Value.GetString("PurgeScope") == IdentityTenantPurgeScopeBinding.Fingerprint(scope);
    }
    public async Task PreviewAsync(Guid tenantId, CancellationToken ct = default)
    {
        RequireSupport(); if (tenantId == Guid.Empty) throw new IdentityValidationException("Tenant scope is required.");
        var page = await Page(tenantId, 1, null, ct); foreach (var row in page.Values) Validate(row, tenantId);
    }
    public async Task<bool> FencePageAsync(IdentityTenantPurgeAuthorization authorization, int pageSize, CancellationToken ct = default)
    {
        RequireSupport(); Bound(pageSize); await authorization.RequireLeaseAsync(ct);
        var tenant = authorization.Scope.TenantId;
        if (key == "tenant-metadata") await DisableTenant(authorization, ct);
        var root = await IdentityTenantPurgePartition.Ensure(table, IdentityTenantPurgePartition.RootPartition(tenant), IdentityTenantPurgePartition.RootKey, ct);
        if (IdentityTenantPurgePartition.Fenced(root) && root.GetString("PurgeScope") != authorization.ScopeHash) throw new IdentityConcurrencyException();
        if (!IdentityTenantPurgePartition.Fenced(root)) {
            root["PurgeScope"] = authorization.ScopeHash; root["PurgeJobId"] = authorization.Scope.JobId; root["Phase"] = "discover";
            root = await SaveRoot(root, authorization, ct);
        }
        if (root.GetString("Phase") == "done") return true;
        if (root.GetString("Phase") == "discover") {
            var page = await Page(tenant, pageSize, root.GetString("Cursor"), ct);
            foreach (var row in page.Values) {
                Validate(row, tenant); await authorization.RequireLeaseAsync(ct);
                var registry = new TableEntity(root.PartitionKey, IdentityTenantPurgePartition.RegistryKey(row.PartitionKey)) { ["TargetPartition"] = row.PartitionKey, ["Status"] = "PurgeControl" };
                try { await table.AddEntityAsync(registry, ct); }
                catch (RequestFailedException ex) when (ex.Status == 409) { }
                if (key == "tenant-invitations" && row.PartitionKey == "TEN|" + tenant.ToString("D") && row.GetString("TokenHash") is { Length: > 0 } hash) {
                    // Legacy primaries prove the current token partition even if its index tag is damaged.
                    var tokenPartition = "TOK|" + hash;
                    var tokenRegistry = new TableEntity(root.PartitionKey, IdentityTenantPurgePartition.RegistryKey(tokenPartition)) { ["TargetPartition"] = tokenPartition, ["Status"] = "PurgeControl" };
                    await authorization.RequireLeaseAsync(ct);
                    try { await table.AddEntityAsync(tokenRegistry, ct); } catch (RequestFailedException ex) when (ex.Status == 409) { }
                }
            }
            root["Cursor"] = page.ContinuationToken;
            if (page.ContinuationToken is not null) { await SaveRoot(root, authorization, ct); return false; }
            root["Phase"] = "registry"; root = await SaveRoot(root, authorization, ct);
        }
        var prefix = IdentityTenantPurgePartition.Prefix + "PART|";
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {root.PartitionKey} and RowKey ge {prefix} and RowKey lt {prefix + '\uffff'}");
        await foreach (var page in table.QueryAsync<TableEntity>(filter, pageSize, cancellationToken: ct).AsPages(root.GetString("Cursor"), pageSize).WithCancellation(ct)) {
            foreach (var registry in page.Values) {
                var partition = registry.GetString("TargetPartition") ?? throw new IdentityProviderException("Tenant writer registry is invalid.");
                if (registry.RowKey != IdentityTenantPurgePartition.RegistryKey(partition)) throw new IdentityProviderException("Tenant writer registry is invalid.");
                await FenceLocal(partition, authorization, ct);
            }
            root["Cursor"] = page.ContinuationToken;
            if (page.ContinuationToken is not null) { await SaveRoot(root, authorization, ct); return false; }
            break;
        }
        root["Phase"] = "done"; root["Cursor"] = null; await SaveRoot(root, authorization, ct); return true;
    }
    public async Task RequireFencedAsync(IdentityTenantPurgeAuthorization authorization, CancellationToken ct = default)
    { if (!await IsFencedAsync(authorization, ct)) throw new IdentityUnauthorizedException("Tenant Identity write fencing is unavailable."); }
    public async Task<bool> IsFencedAsync(IdentityTenantPurgeAuthorization authorization, CancellationToken ct = default)
    {
        RequireSupport(); await authorization.RequireLeaseAsync(ct);
        var root = await table.GetEntityIfExistsAsync<TableEntity>(IdentityTenantPurgePartition.RootPartition(authorization.Scope.TenantId), IdentityTenantPurgePartition.RootKey, cancellationToken: ct);
        if (root.HasValue && IdentityTenantPurgePartition.Fenced(root.Value) && root.Value.GetString("PurgeScope") != authorization.ScopeHash)
            throw new IdentityUnauthorizedException("Tenant Identity write fencing is unavailable.");
        return root.HasValue && root.Value.GetString("PurgeScope") == authorization.ScopeHash && root.Value.GetString("Phase") == "done";
    }
    public async Task<bool> CleanupPageAsync(IdentityTenantPurgeAuthorization authorization, int pageSize, CancellationToken ct = default)
    {
        Bound(pageSize); await RequireFencedAsync(authorization, ct);
        var page = await Page(authorization.Scope.TenantId, pageSize, null, ct);
        foreach (var group in page.Values.GroupBy(x => x.PartitionKey)) {
            foreach (var row in group) Validate(row, authorization.Scope.TenantId);
            var guard = (await table.GetEntityAsync<TableEntity>(group.Key, IdentityTenantPurgePartition.LocalKey(authorization.Scope.TenantId), cancellationToken: ct)).Value;
            if (guard.GetString("PurgeScope") != authorization.ScopeHash) throw new IdentityUnauthorizedException("Tenant Identity write fencing is unavailable.");
            guard["Revision"] = Guid.NewGuid();
            var actions = new List<TableTransactionAction> { new(TableTransactionActionType.UpdateReplace, guard, guard.ETag) };
            actions.AddRange(group.Select(row => new TableTransactionAction(TableTransactionActionType.Delete, row, row.ETag)));
            await authorization.RequireLeaseAsync(ct);
            try { await table.SubmitTransactionAsync(actions, ct); }
            catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412) { throw new IdentityConcurrencyException(); }
        }
        return page.ContinuationToken is null && await VerifyEmptyAsync(authorization, ct);
    }
    public async Task<bool> VerifyEmptyAsync(IdentityTenantPurgeAuthorization authorization, CancellationToken ct = default)
    {
        await RequireFencedAsync(authorization, ct); return (await Page(authorization.Scope.TenantId, 1, null, ct)).Values.Count == 0;
    }
    private async Task DisableTenant(IdentityTenantPurgeAuthorization authorization, CancellationToken ct)
    {
        var tenant = authorization.Scope.TenantId;
        var local = await IdentityTenantPurgePartition.Ensure(table, "TEN", IdentityTenantPurgePartition.LocalKey(tenant), ct);
        if (IdentityTenantPurgePartition.Fenced(local)) {
            if (local.GetString("PurgeScope") != authorization.ScopeHash) throw new IdentityConcurrencyException();
            return;
        }
        var row = await table.GetEntityIfExistsAsync<TableEntity>("TEN", tenant.ToString("D"), cancellationToken: ct);
        if (!row.HasValue || row.Value.ETag.ToString() != authorization.Scope.ExpectedTenantVersion) throw new IdentityConcurrencyException();
        local["PurgeScope"] = authorization.ScopeHash; local["PurgeJobId"] = authorization.Scope.JobId;
        var patch = new TableEntity("TEN", tenant.ToString("D")) { ["Status"] = "Disabled", ["UpdatedAt"] = DateTimeOffset.UtcNow };
        await authorization.RequireLeaseAsync(ct);
        try { await table.SubmitTransactionAsync(new[] {
            new TableTransactionAction(TableTransactionActionType.UpdateReplace, local, local.ETag),
            new TableTransactionAction(TableTransactionActionType.UpdateMerge, patch, row.Value.ETag) }, ct); }
        catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412) { throw new IdentityConcurrencyException(); }
    }
    private async Task FenceLocal(string partition, IdentityTenantPurgeAuthorization authorization, CancellationToken ct)
    {
        var tenant = authorization.Scope.TenantId;
        ValidatePartition(partition, tenant);
        if (key == "tenant-invitations" && partition.StartsWith("TOK|", StringComparison.Ordinal)) {
            var prefix = IdentityTenantPurgePartition.Prefix;
            var tokenFilter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and (RowKey lt {prefix} or RowKey ge {prefix + '\uffff'})");
            await foreach (var row in table.QueryAsync<TableEntity>(tokenFilter, 2, Columns, ct)) {
                if (row.GetString("TenantId") is not { } tag || !Guid.TryParse(tag, out var owner) || owner != tenant)
                    throw new IdentityProviderException("Token index ownership is ambiguous.");
            }
        }
        var local = await IdentityTenantPurgePartition.Ensure(table, partition, IdentityTenantPurgePartition.LocalKey(tenant), ct);
        if (IdentityTenantPurgePartition.Fenced(local)) {
            if (local.GetString("PurgeScope") != authorization.ScopeHash) throw new IdentityConcurrencyException();
            return;
        }
        local["PurgeScope"] = authorization.ScopeHash; local["PurgeJobId"] = authorization.Scope.JobId;
        var actions = new List<TableTransactionAction> { new(TableTransactionActionType.UpdateReplace, local, local.ETag) };
        if (key == "user-membership-indexes") {
            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and (RowKey eq {"TEN|" + tenant.ToString("D")} or RowKey eq {"TEN#" + tenant.ToString("D")})");
            await foreach (var index in table.QueryAsync<TableEntity>(filter, 2, Columns, ct)) {
                Validate(index, tenant); actions.Add(new(TableTransactionActionType.UpdateMerge,
                    new TableEntity(partition, index.RowKey) { ["Status"] = "Disabled", ["IsDefault"] = false }, index.ETag));
            }
        }
        await authorization.RequireLeaseAsync(ct);
        try { await table.SubmitTransactionAsync(actions, ct); }
        catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412) { throw new IdentityConcurrencyException(); }
    }
    private async Task<TableEntity> SaveRoot(TableEntity root, IdentityTenantPurgeAuthorization authorization, CancellationToken ct)
    {
        await authorization.RequireLeaseAsync(ct);
        try {
            var response = await table.UpdateEntityAsync(root, root.ETag, TableUpdateMode.Replace, ct);
            root.ETag = response.Headers.ETag ?? throw new IdentityProviderException("Tenant purge version is unavailable."); return root;
        } catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new IdentityConcurrencyException(); }
    }
    private async Task<Page<TableEntity>> Page(Guid tenant, int size, string? cursor, CancellationToken ct)
    {
        await foreach (var page in table.QueryAsync<TableEntity>(Filter(tenant), size, Columns, ct).AsPages(cursor, size).WithCancellation(ct)) return page;
        return Azure.Page<TableEntity>.FromValues([], null, new EmptyResponse());
    }
    private string Filter(Guid tenant)
    {
        var t = tenant.ToString("D"); var pk = "TEN|" + t;
        var filter = key switch {
            "tenant-metadata" => TableClient.CreateQueryFilter($"PartitionKey eq {"TEN"} and RowKey eq {t}"),
            "user-membership-indexes" => TableClient.CreateQueryFilter($"RowKey eq {pk} or RowKey eq {"TEN#" + t}"),
            "tenant-invitations" => TableClient.CreateQueryFilter($"PartitionKey eq {pk} or TenantId eq {t}"),
            "tenant-agent-credentials" or "tenant-oauth-consents" => TableClient.CreateQueryFilter($"PartitionKey ge {pk + "|"} and PartitionKey lt {pk + "|\uffff"}"),
            _ => TableClient.CreateQueryFilter($"PartitionKey eq {pk}")
        };
        var prefix = IdentityTenantPurgePartition.Prefix;
        return "(" + filter + ") and " + TableClient.CreateQueryFilter($"(RowKey lt {prefix} or RowKey ge {prefix + '\uffff'})");
    }
    private void Validate(TableEntity row, Guid tenant)
    {
        ValidatePartition(row.PartitionKey, tenant);
        var tagged = row.GetString("TenantId");
        if (tagged is not null && (!Guid.TryParse(tagged, out var id) || id != tenant)) throw new IdentityProviderException("Tenant Identity scope is ambiguous.");
        if (key == "tenant-metadata" && row.RowKey != tenant.ToString("D") || key == "user-membership-indexes" && row.RowKey != "TEN|" + tenant.ToString("D") && row.RowKey != "TEN#" + tenant.ToString("D"))
            throw new IdentityProviderException("Tenant Identity scope is ambiguous.");
    }
    private void ValidatePartition(string partition, Guid tenant)
    {
        var pk = "TEN|" + tenant.ToString("D");
        var valid = key switch {
            "tenant-metadata" => partition == "TEN",
            "user-membership-indexes" => partition.StartsWith("USR|", StringComparison.Ordinal) && Guid.TryParseExact(partition[4..], "D", out var user) && user != Guid.Empty,
            "tenant-invitations" => partition == pk || partition.StartsWith("TOK|", StringComparison.Ordinal),
            "tenant-agent-credentials" or "tenant-oauth-consents" => partition.StartsWith(pk + "|", StringComparison.Ordinal),
            _ => partition == pk
        };
        if (!valid) throw new IdentityProviderException("Tenant Identity scope is ambiguous.");
    }
    private void RequireSupport() { if (!ProtectsAllWriters) throw new IdentityProviderException("Native tenant purge writer protection is required."); }
    private static void Bound(int size) { if (size is < 1 or > 25) throw new IdentityValidationException("Tenant cleanup page is invalid."); }
    private sealed class EmptyResponse : Response
    {
        public override int Status => 200; public override string ReasonPhrase => "OK"; public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = ""; public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
        protected override bool TryGetHeader(string name, out string? value) { value = null; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string>? values) { values = null; return false; }
    }
}
public static class AzureTableIdentityTenantPurgeExtensions
{
    public static IServiceCollection AddIBeamIdentityAzureTableTenantPurge(this IServiceCollection services)
    {
        foreach (var key in IdentityTenantPurgeInventory.Keys) {
            var resource = key;
            services.AddScoped<IIdentityTenantPurgeResourceStore>(p => {
                var options = p.GetRequiredService<IOptions<AzureTableIdentityOptions>>(); var o = options.Value;
                var name = resource switch {
                    "tenant-metadata" => o.TenantsTableName, "tenant-memberships" => o.TenantUsersTableName, "user-membership-indexes" => o.UserTenantsTableName,
                    "tenant-roles" => o.TenantRolesTableName, "tenant-invitations" => o.TenantInvitesTableName, "tenant-api-credentials" => o.ApiCredentialsTableName,
                    "tenant-agent-users" => o.AgentUsersTableName, "tenant-agent-credentials" => o.AgentUserCredentialsTableName,
                    "tenant-oauth-consents" => o.OAuthConsentsTableName, "tenant-access-catalog-overrides" => o.AccessCatalogOverridesTableName,
                    _ => throw new InvalidOperationException("Unsupported Identity purge resource.") };
                return new AzureTableIdentityTenantPurgeResourceStore(p.GetRequiredService<TableServiceClient>().GetTableClient(o.FullTableName(name)), resource, options);
            });
        }
        return services;
    }
}
