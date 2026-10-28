using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Exceptions;
namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed partial class AzureTableTenantMembershipStore : IIdentityTenantOwnershipConditionalStore
{
    public int OwnershipProtocolVersion => 1;
    public bool RequiresOwnershipProtection => _opts.RequireOwnershipProtection;
    public async Task ConfigureOwnershipProtectionAsync(Guid tenant, IReadOnlyList<Guid> owners, CancellationToken ct = default)
    {
        if (!_opts.RequireOwnershipProtection || tenant == Guid.Empty || owners is not { Count: > 0 and <= 8 } ||
            owners.Any(x => x == Guid.Empty) || owners.Distinct().Count() != owners.Count)
            throw new IdentityValidationException("Explicit native ownership protection is required.");
        var table = GetTenantUsersTable(); var guard = await TenantMembershipPartition.Read(table, _opts.TenantUsersPk(tenant), ct, _opts.RequireTenantPurgeFencing);
        var expected = string.Join(',', owners.Order().Select(x => x.ToString("D"))); var current = guard.GetString("OwnerRoleIds");
        if (!string.IsNullOrEmpty(current)) {
            if (current == expected && guard.GetInt32("OwnershipProtocolVersion") == 1) return;
            throw new IdentityConcurrencyException(); // Sticky policy: changing owner definitions requires a trusted migration.
        }
        if ((guard.GetInt32("OwnershipProtocolVersion") ?? 0) != 0) throw new IdentityConcurrencyException();
        guard["OwnerRoleIds"] = expected; guard["OwnershipProtocolVersion"] = 1; guard["Revision"] = Guid.NewGuid();
        try { await IdentityTenantPurgePartition.Update(table, guard, guard.ETag, TableUpdateMode.Merge, _opts.RequireTenantPurgeFencing, ct); }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412) { throw new IdentityConcurrencyException(); }
    }
    public async Task ConfigureProtectionAsync(Guid tenant, IReadOnlyList<Guid> administrators, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty || administrators is not { Count: > 0 and <= 8 } || administrators.Any(x => x == Guid.Empty)) throw new IdentityValidationException("Membership protection is invalid.");
        var table = GetTenantUsersTable(); var guard = await TenantMembershipPartition.Read(table, _opts.TenantUsersPk(tenant), ct, _opts.RequireTenantPurgeFencing);
        var expected = string.Join(',', administrators.Distinct().Order().Select(x => x.ToString("D"))); var current = guard.GetString("AdministratorRoleIds");
        if (!string.IsNullOrEmpty(current)) {
            if (current == expected) return;
            // Host-validated immutable administrator roles may expand protection; removal requires an explicit migration.
            if (Ids(current).Except(administrators).Any()) throw new IdentityConcurrencyException();
        }
        guard["AdministratorRoleIds"] = expected; guard["Revision"] = Guid.NewGuid();
        try { await IdentityTenantPurgePartition.Update(table, guard, guard.ETag, TableUpdateMode.Merge, _opts.RequireTenantPurgeFencing, ct); }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412) { throw new IdentityConcurrencyException(); }
    }
    public Task<IdentityTenantMembershipSnapshot> ReadSnapshotAsync(Guid tenant, int maximumMembers, CancellationToken ct = default)
        => ReadSnapshotCoreAsync(tenant, maximumMembers, false, ct);
    private async Task<IdentityTenantMembershipSnapshot> ReadSnapshotCoreAsync(Guid tenant, int maximumMembers, bool existingGuardOnly, CancellationToken ct)
    {
        if (tenant == Guid.Empty || maximumMembers is < 1 or > 5000) throw new IdentityValidationException("Membership snapshot is invalid.");
        var table = GetTenantUsersTable(); var pk = _opts.TenantUsersPk(tenant);
        TableEntity guard;
        if (existingGuardOnly) {
            var existing = await table.GetEntityIfExistsAsync<TableEntity>(pk, TenantMembershipPartition.GuardKey, cancellationToken: ct);
            if (!existing.HasValue) throw new IdentityValidationException("Configure native ownership protection before ownership administration.");
            guard = existing.Value;
        } else guard = await TenantMembershipPartition.Read(table, pk, ct, _opts.RequireTenantPurgeFencing);
        var rows = new List<IdentityTenantMembershipState>();
        var prefix = _opts.TenantUsersRk("");
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {pk} and RowKey ge {prefix} and RowKey lt {prefix + '\uffff'}");
        var columns = new[] { "PartitionKey", "RowKey", "TenantId", "UserId", "Status", "RoleIdsCsv", "RolesCsv", "IBeamLastMembershipOperationId" };
        await foreach (var page in table.QueryAsync<TableEntity>(filter, Math.Min(1000, maximumMembers + 1), columns, ct).AsPages()) {
            foreach (var row in page.Values) { rows.Add(State(row, tenant)); if (rows.Count > maximumMembers) throw new IdentityValidationException("Membership snapshot exceeds the configured bound."); }
        }
        var after = (await table.GetEntityAsync<TableEntity>(pk, TenantMembershipPartition.GuardKey, cancellationToken: ct)).Value;
        if (after.ETag != guard.ETag) throw new IdentityConcurrencyException();
        var owners = Ids(guard.GetString("OwnerRoleIds")); var protocol = guard.GetInt32("OwnershipProtocolVersion") ?? 0;
        if (owners.Count > 8 || (owners.Count == 0 ? protocol != 0 : protocol != 1)) throw new IdentityProviderException("Ownership protection metadata is invalid.");
        return new(tenant, guard.ETag.ToString(), Ids(guard.GetString("AdministratorRoleIds")), rows) { ProtectedOwnerRoleIds = owners, OwnershipProtocolVersion = protocol };
    }
    public Task<IdentityTenantMembershipState?> FindStateAsync(Guid tenant, Guid user, CancellationToken ct = default) => ReadStateAsync(tenant, user, false, ct);
    private async Task<IdentityTenantMembershipState?> ReadStateAsync(Guid tenant, Guid user, bool managedOnly, CancellationToken ct)
    {
        if (tenant == Guid.Empty || user == Guid.Empty) throw new IdentityValidationException("Membership scope is invalid.");
        var row = await GetTenantUsersTable().GetEntityIfExistsAsync<TableEntity>(_opts.TenantUsersPk(tenant), _opts.TenantUsersRk(user.ToString("D")),
            ["PartitionKey", "RowKey", "TenantId", "UserId", "Status", "RoleIdsCsv", "RolesCsv", "IBeamLastMembershipOperationId"], ct);
        var result = row.HasValue && (!managedOnly || row.Value.GetString("IBeamLastMembershipOperationId") is not null) ? State(row.Value, tenant) : null;
        if (result is not null && result.UserId != user) throw new IdentityProviderException("Membership scope is invalid."); return result;
    }
    public async Task<IdentityTenantMembershipState> ReplaceStateAsync(IdentityTenantMembershipState next, string memberVersion, string tenantVersion, Guid operation, CancellationToken ct = default)
    {
        if (next.TenantId == Guid.Empty || next.UserId == Guid.Empty || operation == Guid.Empty || string.IsNullOrWhiteSpace(memberVersion) || memberVersion == "*" ||
            string.IsNullOrWhiteSpace(tenantVersion) || tenantVersion == "*") throw new IdentityConcurrencyException();
        var table = GetTenantUsersTable(); var pk = _opts.TenantUsersPk(next.TenantId);
        var current = await FindStateAsync(next.TenantId, next.UserId, ct) ?? throw new IdentityNotFoundException("Membership was not found.");
        if (current.LastOperationId == operation) return current;
        if (current.Version != memberVersion) throw new IdentityConcurrencyException();
        var guard = await TenantMembershipPartition.Read(table, pk, ct, _opts.RequireTenantPurgeFencing);
        if (_opts.RequireOwnershipProtection && (string.IsNullOrEmpty(guard.GetString("OwnerRoleIds")) || guard.GetInt32("OwnershipProtocolVersion") != 1))
            throw new IdentityValidationException("Ownership protection must be configured before versioned membership writes.");
        var row = (await table.GetEntityAsync<TableEntity>(pk, _opts.TenantUsersRk(next.UserId.ToString("D")), cancellationToken: ct)).Value;
        if (row.ETag.ToString() != memberVersion) throw new IdentityConcurrencyException();
        row["Status"] = next.IsEnabled ? "Active" : "Disabled"; row["RoleIdsCsv"] = string.Join(',', next.RoleIds.Select(x => x.ToString("D")));
        row["RolesCsv"] = string.Join(',', next.RoleNames); row["IBeamLastMembershipOperationId"] = operation.ToString("D"); row["UpdatedAt"] = DateTimeOffset.UtcNow;
        if (!next.IsEnabled) row["DisabledAt"] = DateTimeOffset.UtcNow;
        else { row.Remove("DisabledAt"); row.Remove("DisabledReason"); }
        try { await TenantMembershipPartition.Commit(table, row, false, guard, new ETag(tenantVersion), ct, true, _opts.RequireTenantPurgeFencing); }
        catch (RequestFailedException ex) when (ex.Status is 404 or 409 or 412) { throw new IdentityConcurrencyException(); }
        return await FindStateAsync(next.TenantId, next.UserId, ct) ?? throw new IdentityConcurrencyException();
    }
    public async Task RepairProjectionAsync(Guid tenant, Guid user, CancellationToken ct = default)
    {
        var current = await FindStateAsync(tenant, user, ct) ?? throw new IdentityNotFoundException("Membership was not found.");
        var table = GetUserTenantsTable(); var pk = _opts.UserTenantsPk(user.ToString("D")); var rk = _opts.UserTenantsRk(tenant);
        var secondary = await table.GetEntityIfExistsAsync<TableEntity>(pk, rk, cancellationToken: ct);
        if (!secondary.HasValue) throw new IdentityConcurrencyException(); // Never invent global/default membership state during repair.
        var row = new TableEntity(pk, rk) { ["Status"] = current.IsEnabled ? "Active" : "Disabled", ["RolesCsv"] = string.Join(',', current.RoleNames),
            ["RoleIdsCsv"] = string.Join(',', current.RoleIds.Select(x => x.ToString("D"))) };
        if (!current.IsEnabled) row["DisabledAt"] = DateTimeOffset.UtcNow;
        try { await IdentityTenantPurgePartition.Update(table, row, secondary.Value.ETag, TableUpdateMode.Merge, _opts.RequireTenantPurgeFencing, ct); }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new IdentityConcurrencyException(); }
        if ((await FindStateAsync(tenant, user, ct))?.Version != current.Version) throw new IdentityConcurrencyException();
    }
    private static IReadOnlyList<Guid> Ids(string? csv)
    {
        var values = (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length > 32 || values.Any(x => !Guid.TryParse(x, out var id) || id == Guid.Empty)) throw new IdentityProviderException("Membership roles are invalid.");
        return values.Select(Guid.Parse).Distinct().ToArray();
    }
    private static IdentityTenantMembershipState State(TableEntity row, Guid tenant)
    {
        var status = row.GetString("Status");
        if (!Guid.TryParseExact(row.GetString("TenantId"), "D", out var actual) || actual != tenant || !Guid.TryParseExact(row.GetString("UserId"), "D", out var user) || user == Guid.Empty ||
            row.PartitionKey != "TEN|" + tenant.ToString("D") || row.RowKey != "USR|" + user.ToString("D") ||
            !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) && !string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase)) throw new IdentityProviderException("Membership scope/state is invalid.");
        var names = (row.GetString("RolesCsv") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length > 32 || names.Any(x => x.Length > 100 || x.Any(char.IsControl))) throw new IdentityProviderException("Membership roles are invalid.");
        var op = row.GetString("IBeamLastMembershipOperationId");
        if (op is not (null or "native") && (!Guid.TryParseExact(op, "D", out var parsed) || parsed == Guid.Empty)) throw new IdentityProviderException("Membership operation is invalid.");
        return new(tenant, user, string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase), Ids(row.GetString("RoleIdsCsv")), names, row.ETag.ToString(), op is null or "native" ? null : Guid.Parse(op));
    }
}
