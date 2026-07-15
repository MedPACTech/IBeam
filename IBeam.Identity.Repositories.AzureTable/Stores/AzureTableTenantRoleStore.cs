using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableTenantRoleStore : ITenantRoleStore
{
    public const string OwnerRoleName = "Owner";
    public const string AdminRoleName = "Administrator";

    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableTenantRoleStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<IReadOnlyList<TenantRole>> GetRolesAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var list = await GetActiveRoleEntitiesAsync(tenantId, ct).ConfigureAwait(false);
            return list.Select(Map).ToList();
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<TenantRole?> GetRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var entity = await GetRoleEntityAsync(tenantId, roleId, ct).ConfigureAwait(false);
            if (entity is null || !IsActive(entity.Status))
                return null;

            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<TenantRole> CreateRoleAsync(Guid tenantId, string name, bool isSystem = false, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var roleName = NormalizeName(name);
            var normalized = roleName.ToUpperInvariant();

            await EnsureRoleNameUniqueAsync(tenantId, normalized, roleIdToExclude: null, ct).ConfigureAwait(false);

            var roleId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var entity = new TenantRoleEntity
            {
                PartitionKey = _opts.TenantRolesPk(tenantId),
                RowKey = _opts.TenantRolesRk(roleId),
                TenantId = tenantId.ToString("D"),
                RoleId = roleId.ToString("D"),
                Name = roleName,
                NormalizedName = normalized,
                IsSystem = isSystem,
                Status = "Active",
                CreatedAt = now
            };

            await RolesTable().AddEntityAsync(entity, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<TenantRole> UpdateRoleAsync(Guid tenantId, Guid roleId, string name, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var existing = await GetRoleEntityAsync(tenantId, roleId, ct).ConfigureAwait(false)
                ?? throw new IdentityNotFoundException($"Role '{roleId}' was not found.");

            if (!IsActive(existing.Status))
                throw new IdentityNotFoundException($"Role '{roleId}' was not found.");
            if (existing.IsSystem)
                throw new IdentityValidationException("System roles cannot be renamed.");

            var roleName = NormalizeName(name);
            var normalized = roleName.ToUpperInvariant();

            await EnsureRoleNameUniqueAsync(tenantId, normalized, roleId, ct).ConfigureAwait(false);

            var previousName = existing.Name;
            existing.Name = roleName;
            existing.NormalizedName = normalized;
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            await RolesTable().UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            await RenameRoleInMembershipsAsync(tenantId, roleId, previousName, roleName, ct).ConfigureAwait(false);

            return Map(existing);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task DeleteRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var existing = await GetRoleEntityAsync(tenantId, roleId, ct).ConfigureAwait(false)
                ?? throw new IdentityNotFoundException($"Role '{roleId}' was not found.");

            if (!IsActive(existing.Status))
                return;
            if (existing.IsSystem)
                throw new IdentityValidationException("System roles cannot be deleted.");

            existing.Status = "Disabled";
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            await RolesTable().UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            await RemoveRoleFromMembershipsAsync(tenantId, roleId, existing.Name, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<UserTenantRoleAssignment> GrantRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var roles = await RequireActiveRolesAsync(tenantId, roleIds, ct).ConfigureAwait(false);
            var result = await UpdateMembershipRolesAsync(tenantId, userId, current => current.Union(roleIds).ToHashSet(), roles, ct)
                .ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<UserTenantRoleAssignment> EnsureTenantMembershipAndGrantRolesAsync(TenantMembershipRoleBootstrapRequest request, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            await EnsureTenantAsync(request.TenantId, request.UserId, request.TenantName, ct).ConfigureAwait(false);
            await EnsureDefaultRolesAsync(request.TenantId, ct).ConfigureAwait(false);

            var requestedRoles = new List<TenantRole>();
            if (request.RoleIds is { Count: > 0 })
                requestedRoles.AddRange(await RequireActiveRolesAsync(request.TenantId, request.RoleIds, ct).ConfigureAwait(false));

            if (request.RoleNames is { Count: > 0 })
            {
                foreach (var roleName in request.RoleNames)
                {
                    var role = await GetOrCreateRoleByNameAsync(request.TenantId, roleName, ct).ConfigureAwait(false);
                    requestedRoles.Add(role);
                }
            }

            var distinctRoles = requestedRoles
                .GroupBy(x => x.RoleId)
                .Select(x => x.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await EnsureMembershipAsync(
                request,
                ct).ConfigureAwait(false);

            return await UpdateMembershipRolesAsync(
                request.TenantId,
                request.UserId,
                current => current.Union(distinctRoles.Select(x => x.RoleId)).ToHashSet(),
                (await GetRoleMapAsync(request.TenantId, ct).ConfigureAwait(false)).Values.ToList(),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<UserTenantRoleAssignment> RevokeRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var roleIdsSet = roleIds.ToHashSet();
            var roles = await GetRoleMapAsync(tenantId, ct).ConfigureAwait(false);
            var result = await UpdateMembershipRolesAsync(tenantId, userId, current =>
            {
                current.ExceptWith(roleIdsSet);
                return current;
            }, roles.Values.ToList(), ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<TenantRole>> GetRolesForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var membership = await TenantUsersTable()
                .GetEntityIfExistsAsync<TenantUserEntity>(_opts.TenantUsersPk(tenantId), _opts.TenantUsersRk(userId.ToString("D")), cancellationToken: ct)
                .ConfigureAwait(false);

            if (!membership.HasValue || !IsActive(membership.Value.Status))
                return Array.Empty<TenantRole>();

            var activeRoleMap = await GetRoleMapAsync(tenantId, ct).ConfigureAwait(false);

            var roleIds = ParseGuidCsv(membership.Value.RoleIdsCsv);
            var list = roleIds
                .Where(activeRoleMap.ContainsKey)
                .Select(x => activeRoleMap[x])
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (list.Count > 0)
                return list;

            // Backward-compat fallback for memberships without RoleIdsCsv.
            var roleNames = ParseCsv(membership.Value.RolesCsv).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return activeRoleMap.Values
                .Where(x => roleNames.Contains(x.Name))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task EnsureDefaultRolesAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await EnsureRoleByNameAsync(tenantId, OwnerRoleName, isSystem: true, ct).ConfigureAwait(false);
            await EnsureRoleByNameAsync(tenantId, AdminRoleName, isSystem: true, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private async Task<UserTenantRoleAssignment> UpdateMembershipRolesAsync(
        Guid tenantId,
        Guid userId,
        Func<HashSet<Guid>, HashSet<Guid>> roleIdMutation,
        IReadOnlyList<TenantRole> activeRoles,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return await UpdateMembershipRolesOnceAsync(tenantId, userId, roleIdMutation, activeRoles, ct)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
            {
            }
        }

        throw new IdentityProviderException("Failed to update tenant membership roles due to concurrent updates.");
    }

    private async Task<UserTenantRoleAssignment> UpdateMembershipRolesOnceAsync(
        Guid tenantId,
        Guid userId,
        Func<HashSet<Guid>, HashSet<Guid>> roleIdMutation,
        IReadOnlyList<TenantRole> activeRoles,
        CancellationToken ct)
    {
        var userIdStr = userId.ToString("D");
        var tenantUserResp = await TenantUsersTable()
            .GetEntityIfExistsAsync<TenantUserEntity>(_opts.TenantUsersPk(tenantId), _opts.TenantUsersRk(userIdStr), cancellationToken: ct)
            .ConfigureAwait(false);
        var userTenantResp = await UserTenantsTable()
            .GetEntityIfExistsAsync<UserTenantEntity>(_opts.UserTenantsPk(userIdStr), _opts.UserTenantsRk(tenantId), cancellationToken: ct)
            .ConfigureAwait(false);

        if (!tenantUserResp.HasValue || !userTenantResp.HasValue)
            throw new IdentityValidationException($"User '{userId}' is not a member of tenant '{tenantId}'.");

        var tenantUser = tenantUserResp.Value;
        var userTenant = userTenantResp.Value;

        var roleMap = activeRoles.ToDictionary(x => x.RoleId, x => x);
        var current = ParseGuidCsv(tenantUser.RoleIdsCsv);
        current = roleIdMutation(current);

        var effectiveRoles = current
            .Where(roleMap.ContainsKey)
            .Select(x => roleMap[x])
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var roleIdsCsv = string.Join(",", effectiveRoles.Select(x => x.RoleId.ToString("D")));
        var rolesCsv = string.Join(",", effectiveRoles.Select(x => x.Name));

        tenantUser.RoleIdsCsv = roleIdsCsv;
        tenantUser.RolesCsv = rolesCsv;
        userTenant.RoleIdsCsv = roleIdsCsv;
        userTenant.RolesCsv = rolesCsv;

        await TenantUsersTable().UpdateEntityAsync(tenantUser, tenantUser.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        await UserTenantsTable().UpdateEntityAsync(userTenant, userTenant.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);

        return new UserTenantRoleAssignment(tenantId, userId, effectiveRoles);
    }

    private async Task EnsureTenantAsync(Guid tenantId, Guid userId, string? tenantName, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var name = string.IsNullOrWhiteSpace(tenantName) ? "Workspace" : tenantName.Trim();
        var entity = new TenantEntity
        {
            PartitionKey = TenantEntity.TenantsPartitionKey,
            RowKey = tenantId.ToString("D"),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Status = "Active",
            CreatedAt = now
        };

        try
        {
            await TenantsTable().AddEntityAsync(entity, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            var existing = await TenantsTable()
                .GetEntityIfExistsAsync<TenantEntity>(TenantEntity.TenantsPartitionKey, tenantId.ToString("D"), cancellationToken: ct)
                .ConfigureAwait(false);

            if (!existing.HasValue)
                return;

            var tenant = existing.Value;
            var changed = false;
            if (!IsActive(tenant.Status))
            {
                tenant.Status = "Active";
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(tenantName) && !string.Equals(tenant.Name, name, StringComparison.Ordinal))
            {
                tenant.Name = name;
                tenant.NormalizedName = name.ToUpperInvariant();
                changed = true;
            }

            if (changed)
            {
                tenant.UpdatedAt = now;
                await TenantsTable().UpdateEntityAsync(tenant, tenant.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task EnsureMembershipAsync(TenantMembershipRoleBootstrapRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = request.TenantId;
        var userId = request.UserId;
        var userIdStr = userId.ToString("D");
        var tenantIdStr = tenantId.ToString("D");
        var tenantName = request.TenantName;
        var setAsDefault = request.SetAsDefault;
        var displayName = string.IsNullOrWhiteSpace(tenantName) ? null : tenantName.Trim();
        var userDisplayName = NormalizeOptional(request.UserDisplayName);
        var userEmail = NormalizeEmail(request.UserEmail);

        var tenantUsers = TenantUsersTable();
        var tenantUserPk = _opts.TenantUsersPk(tenantId);
        var tenantUserRk = _opts.TenantUsersRk(userIdStr);
        var tenantUserResp = await tenantUsers
            .GetEntityIfExistsAsync<TenantUserEntity>(tenantUserPk, tenantUserRk, cancellationToken: ct)
            .ConfigureAwait(false);

        if (tenantUserResp.HasValue)
        {
            var tenantUser = tenantUserResp.Value;
            tenantUser.Status = "Active";
            tenantUser.DisabledAt = null;
            tenantUser.DisabledReason = null;
            if (!string.IsNullOrWhiteSpace(userDisplayName))
                tenantUser.UserDisplayName = userDisplayName;
            if (!string.IsNullOrWhiteSpace(userEmail))
                tenantUser.Email = userEmail;
            await tenantUsers.UpdateEntityAsync(tenantUser, tenantUser.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await tenantUsers.AddEntityAsync(new TenantUserEntity
                {
                    PartitionKey = tenantUserPk,
                    RowKey = tenantUserRk,
                    TenantId = tenantIdStr,
                    UserId = userIdStr,
                    Status = "Active",
                    UserDisplayName = userDisplayName,
                    Email = userEmail,
                    CreatedAt = now
                }, ct).ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                var existing = await tenantUsers
                    .GetEntityIfExistsAsync<TenantUserEntity>(tenantUserPk, tenantUserRk, cancellationToken: ct)
                    .ConfigureAwait(false);

                if (!existing.HasValue)
                    throw;

                var tenantUser = existing.Value;
                tenantUser.Status = "Active";
                tenantUser.DisabledAt = null;
                tenantUser.DisabledReason = null;
                if (!string.IsNullOrWhiteSpace(userDisplayName))
                    tenantUser.UserDisplayName = userDisplayName;
                if (!string.IsNullOrWhiteSpace(userEmail))
                    tenantUser.Email = userEmail;
                await tenantUsers.UpdateEntityAsync(tenantUser, tenantUser.ETag, TableUpdateMode.Replace, ct)
                    .ConfigureAwait(false);
            }
        }

        var userTenants = UserTenantsTable();
        var userTenantPk = _opts.UserTenantsPk(userIdStr);
        var userTenantRk = _opts.UserTenantsRk(tenantId);
        var userTenantResp = await userTenants
            .GetEntityIfExistsAsync<UserTenantEntity>(userTenantPk, userTenantRk, cancellationToken: ct)
            .ConfigureAwait(false);

        if (userTenantResp.HasValue)
        {
            var userTenant = userTenantResp.Value;
            userTenant.Status = "Active";
            userTenant.DisabledAt = null;
            userTenant.DisabledReason = null;
            if (!string.IsNullOrWhiteSpace(displayName))
                userTenant.TenantDisplayName = displayName;
            if (setAsDefault)
            {
                userTenant.IsDefault = true;
                userTenant.LastSelectedAt = now;
            }

            await userTenants.UpdateEntityAsync(userTenant, userTenant.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await userTenants.AddEntityAsync(new UserTenantEntity
                {
                    PartitionKey = userTenantPk,
                    RowKey = userTenantRk,
                    UserId = userIdStr,
                    TenantId = tenantIdStr,
                    Status = "Active",
                    TenantDisplayName = displayName,
                    IsDefault = setAsDefault,
                    LastSelectedAt = setAsDefault ? now : null,
                    CreatedAt = now
                }, ct).ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                var existing = await userTenants
                    .GetEntityIfExistsAsync<UserTenantEntity>(userTenantPk, userTenantRk, cancellationToken: ct)
                    .ConfigureAwait(false);

                if (!existing.HasValue)
                    throw;

                var userTenant = existing.Value;
                userTenant.Status = "Active";
                userTenant.DisabledAt = null;
                userTenant.DisabledReason = null;
                if (!string.IsNullOrWhiteSpace(displayName))
                    userTenant.TenantDisplayName = displayName;
                if (setAsDefault)
                {
                    userTenant.IsDefault = true;
                    userTenant.LastSelectedAt = now;
                }

                await userTenants.UpdateEntityAsync(userTenant, userTenant.ETag, TableUpdateMode.Replace, ct)
                    .ConfigureAwait(false);
            }
        }
    }

    private Task<TenantRole> GetOrCreateRoleByNameAsync(Guid tenantId, string name, CancellationToken ct)
        => EnsureRoleByNameAsync(tenantId, name, isSystem: false, ct);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private async Task<TenantRole> EnsureRoleByNameAsync(Guid tenantId, string name, bool isSystem, CancellationToken ct)
    {
        var roleName = NormalizeName(name);
        var normalized = roleName.ToUpperInvariant();
        var existingByName = (await GetActiveRoleEntitiesAsync(tenantId, ct).ConfigureAwait(false))
            .FirstOrDefault(x => string.Equals(x.NormalizedName, normalized, StringComparison.OrdinalIgnoreCase));

        if (existingByName is not null)
        {
            if (isSystem && !existingByName.IsSystem)
            {
                existingByName.IsSystem = true;
                existingByName.UpdatedAt = DateTimeOffset.UtcNow;
                await RolesTable()
                    .UpdateEntityAsync(existingByName, existingByName.ETag, TableUpdateMode.Replace, ct)
                    .ConfigureAwait(false);
            }

            return Map(existingByName);
        }

        var roleId = DeterministicGuid.Create(
            "IBeam.Identity.AzureTable.TenantRole",
            $"{tenantId:D}:{normalized}");
        var now = DateTimeOffset.UtcNow;
        var entity = new TenantRoleEntity
        {
            PartitionKey = _opts.TenantRolesPk(tenantId),
            RowKey = _opts.TenantRolesRk(roleId),
            TenantId = tenantId.ToString("D"),
            RoleId = roleId.ToString("D"),
            Name = roleName,
            NormalizedName = normalized,
            IsSystem = isSystem,
            Status = "Active",
            CreatedAt = now
        };

        try
        {
            await RolesTable().AddEntityAsync(entity, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            var response = await RolesTable()
                .GetEntityIfExistsAsync<TenantRoleEntity>(
                    _opts.TenantRolesPk(tenantId),
                    _opts.TenantRolesRk(roleId),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            if (!response.HasValue)
                throw;

            var existing = response.Value;
            var changed = false;
            if (!IsActive(existing.Status))
            {
                existing.Status = "Active";
                changed = true;
            }

            if (!string.Equals(existing.Name, roleName, StringComparison.Ordinal))
            {
                existing.Name = roleName;
                existing.NormalizedName = normalized;
                changed = true;
            }

            if (isSystem && !existing.IsSystem)
            {
                existing.IsSystem = true;
                changed = true;
            }

            if (changed)
            {
                existing.UpdatedAt = now;
                await RolesTable()
                    .UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace, ct)
                    .ConfigureAwait(false);
            }

            return Map(existing);
        }
    }

    private async Task RenameRoleInMembershipsAsync(Guid tenantId, Guid roleId, string previousName, string nextName, CancellationToken ct)
    {
        var tenantPk = _opts.TenantUsersPk(tenantId);
        var affectedTenantUsers = new List<TenantUserEntity>();
        await foreach (var entity in TenantUsersTable().QueryAsync<TenantUserEntity>(x => x.PartitionKey == tenantPk, cancellationToken: ct))
        {
            var roleIds = ParseGuidCsv(entity.RoleIdsCsv);
            if (!roleIds.Contains(roleId))
                continue;

            var names = ParseCsv(entity.RolesCsv)
                .Where(x => !string.Equals(x, previousName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            names.Add(nextName);

            entity.RolesCsv = string.Join(",", names.Distinct(StringComparer.OrdinalIgnoreCase));
            affectedTenantUsers.Add(entity);
        }

        foreach (var tenantUser in affectedTenantUsers)
        {
            await TenantUsersTable().UpdateEntityAsync(tenantUser, tenantUser.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);

            var userId = tenantUser.UserId;
            var userTenantResp = await UserTenantsTable()
                .GetEntityIfExistsAsync<UserTenantEntity>(_opts.UserTenantsPk(userId), _opts.UserTenantsRk(tenantId), cancellationToken: ct)
                .ConfigureAwait(false);
            if (!userTenantResp.HasValue)
                continue;

            var userTenant = userTenantResp.Value;
            userTenant.RolesCsv = tenantUser.RolesCsv;
            userTenant.RoleIdsCsv = tenantUser.RoleIdsCsv;
            await UserTenantsTable().UpdateEntityAsync(userTenant, userTenant.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        }
    }

    private async Task RemoveRoleFromMembershipsAsync(Guid tenantId, Guid roleId, string roleName, CancellationToken ct)
    {
        var tenantPk = _opts.TenantUsersPk(tenantId);
        var affectedTenantUsers = new List<TenantUserEntity>();

        await foreach (var entity in TenantUsersTable().QueryAsync<TenantUserEntity>(x => x.PartitionKey == tenantPk, cancellationToken: ct))
        {
            var roleIds = ParseGuidCsv(entity.RoleIdsCsv);
            if (!roleIds.Remove(roleId))
                continue;

            var names = ParseCsv(entity.RolesCsv)
                .Where(x => !string.Equals(x, roleName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            entity.RoleIdsCsv = string.Join(",", roleIds.Select(x => x.ToString("D")));
            entity.RolesCsv = string.Join(",", names);
            affectedTenantUsers.Add(entity);
        }

        foreach (var tenantUser in affectedTenantUsers)
        {
            await TenantUsersTable().UpdateEntityAsync(tenantUser, tenantUser.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);

            var userId = tenantUser.UserId;
            var userTenantResp = await UserTenantsTable()
                .GetEntityIfExistsAsync<UserTenantEntity>(_opts.UserTenantsPk(userId), _opts.UserTenantsRk(tenantId), cancellationToken: ct)
                .ConfigureAwait(false);
            if (!userTenantResp.HasValue)
                continue;

            var userTenant = userTenantResp.Value;
            userTenant.RolesCsv = tenantUser.RolesCsv;
            userTenant.RoleIdsCsv = tenantUser.RoleIdsCsv;
            await UserTenantsTable().UpdateEntityAsync(userTenant, userTenant.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<TenantRole>> RequireActiveRolesAsync(Guid tenantId, IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        var ids = roleIds.Distinct().ToList();
        var map = await GetRoleMapAsync(tenantId, ct).ConfigureAwait(false);
        var missing = ids.Where(x => !map.ContainsKey(x)).ToList();
        if (missing.Count > 0)
            throw new IdentityValidationException($"One or more roleIds do not exist in tenant '{tenantId}'.");
        return ids.Select(x => map[x]).ToList();
    }

    private async Task<Dictionary<Guid, TenantRole>> GetRoleMapAsync(Guid tenantId, CancellationToken ct)
    {
        var roles = await GetActiveRoleEntitiesAsync(tenantId, ct).ConfigureAwait(false);
        return roles
            .Select(Map)
            .ToDictionary(x => x.RoleId, x => x);
    }

    private async Task<TenantRoleEntity?> GetRoleEntityAsync(Guid tenantId, Guid roleId, CancellationToken ct)
    {
        var resp = await RolesTable()
            .GetEntityIfExistsAsync<TenantRoleEntity>(_opts.TenantRolesPk(tenantId), _opts.TenantRolesRk(roleId), cancellationToken: ct)
            .ConfigureAwait(false);
        return resp.HasValue ? resp.Value : null;
    }

    private async Task<List<TenantRoleEntity>> GetActiveRoleEntitiesAsync(Guid tenantId, CancellationToken ct)
    {
        var list = new List<TenantRoleEntity>();
        await foreach (var entity in RolesTable().QueryAsync<TenantRoleEntity>(
            x => x.PartitionKey == _opts.TenantRolesPk(tenantId), cancellationToken: ct))
        {
            if (IsActive(entity.Status))
                list.Add(entity);
        }

        return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task EnsureRoleNameUniqueAsync(Guid tenantId, string normalizedName, Guid? roleIdToExclude, CancellationToken ct)
    {
        var roles = await GetActiveRoleEntitiesAsync(tenantId, ct).ConfigureAwait(false);
        var duplicate = roles.FirstOrDefault(x =>
            string.Equals(x.NormalizedName, normalizedName, StringComparison.OrdinalIgnoreCase) &&
            (!roleIdToExclude.HasValue || !string.Equals(x.RoleId, roleIdToExclude.Value.ToString("D"), StringComparison.OrdinalIgnoreCase)));

        if (duplicate is not null)
            throw new IdentityValidationException($"Role '{duplicate.Name}' already exists for tenant '{tenantId}'.");
    }

    private static HashSet<Guid> ParseGuidCsv(string? csv)
    {
        var set = new HashSet<Guid>();
        foreach (var raw in ParseCsv(csv))
        {
            if (Guid.TryParse(raw, out var guid) && guid != Guid.Empty)
                set.Add(guid);
        }

        return set;
    }

    private static IEnumerable<string> ParseCsv(string? csv)
        => (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsActive(string? status)
        => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string name) => name.Trim();

    private static TenantRole Map(TenantRoleEntity x)
        => new(
            TenantId: Guid.Parse(x.TenantId),
            RoleId: Guid.Parse(x.RoleId),
            Name: x.Name,
            IsSystem: x.IsSystem,
            IsActive: IsActive(x.Status),
            CreatedAt: x.CreatedAt,
            UpdatedAt: x.UpdatedAt);

    private TableClient RolesTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.TenantRolesTableName));

    private TableClient TenantsTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.TenantsTableName));

    private TableClient TenantUsersTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.TenantUsersTableName));

    private TableClient UserTenantsTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.UserTenantsTableName));
}
