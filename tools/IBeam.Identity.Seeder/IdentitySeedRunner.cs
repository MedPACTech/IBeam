using IBeam.AccessControl;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Seeder;

internal sealed class IdentitySeedRunner
{
    private readonly IIdentityTenantService _tenants;
    private readonly ITenantRoleService _roles;
    private readonly IIdentityUserStore _users;
    private readonly IPermissionRoleMapService _permissionMaps;
    private readonly IResourceAccessService _resourceAccess;

    public IdentitySeedRunner(
        IIdentityTenantService tenants,
        ITenantRoleService roles,
        IIdentityUserStore users,
        IPermissionRoleMapService permissionMaps,
        IResourceAccessService resourceAccess)
    {
        _tenants = tenants;
        _roles = roles;
        _users = users;
        _permissionMaps = permissionMaps;
        _resourceAccess = resourceAccess;
    }

    public async Task<IdentitySeedResult> RunAsync(
        SeedIdentityConfig config,
        IdentitySeedRunOptions options,
        CancellationToken ct = default)
    {
        var result = new IdentitySeedResult { Applied = options.Apply };
        var tenantByKey = new Dictionary<string, IdentityTenant>(StringComparer.OrdinalIgnoreCase);
        var roleByKey = new Dictionary<(string TenantKey, string RoleKey), TenantRole>();
        var userByKey = new Dictionary<string, IdentityUser>(StringComparer.OrdinalIgnoreCase);

        foreach (var tenantConfig in config.Tenants)
        {
            await CaptureAsync(result, "tenant", tenantConfig.Key, async () =>
            {
                var tenant = await EnsureTenantAsync(tenantConfig, config.Behavior, options, result, ct)
                    .ConfigureAwait(false);
                if (tenant is not null)
                {
                    tenantByKey[tenantConfig.Key] = tenant;
                }
            }).ConfigureAwait(false);
        }

        foreach (var tenantConfig in config.Tenants)
        {
            if (!tenantByKey.ContainsKey(tenantConfig.Key))
            {
                continue;
            }

            foreach (var roleConfig in tenantConfig.Roles)
            {
                await CaptureAsync(result, "role", $"{tenantConfig.Key}.{roleConfig.Key}", async () =>
                {
                    var role = await EnsureRoleAsync(tenantConfig, roleConfig, config.Behavior, options, result, ct)
                        .ConfigureAwait(false);
                    if (role is not null)
                    {
                        roleByKey[(tenantConfig.Key, roleConfig.Key)] = role;
                    }
                }).ConfigureAwait(false);
            }
        }

        foreach (var tenantConfig in config.Tenants)
        {
            if (!tenantByKey.ContainsKey(tenantConfig.Key))
            {
                continue;
            }

            foreach (var mapConfig in tenantConfig.PermissionMappings)
            {
                await CaptureAsync(result, "permission-map", PermissionMapKey(tenantConfig, mapConfig), async () =>
                {
                    await EnsurePermissionMapAsync(tenantConfig, mapConfig, roleByKey, options, result, ct)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
        }

        if (config.BootstrapActor is not null)
        {
            await CaptureAsync(result, "user", config.BootstrapActor.Key, async () =>
            {
                var user = await EnsureUserAsync(config.BootstrapActor, config.Behavior, options, result, ct)
                    .ConfigureAwait(false);
                if (user is not null)
                {
                    userByKey[config.BootstrapActor.Key] = user;
                }
            }).ConfigureAwait(false);
        }

        foreach (var userConfig in config.Users)
        {
            await CaptureAsync(result, "user", userConfig.Key, async () =>
            {
                var user = await EnsureUserAsync(userConfig, config.Behavior, options, result, ct)
                    .ConfigureAwait(false);
                if (user is not null)
                {
                    userByKey[userConfig.Key] = user;
                }
            }).ConfigureAwait(false);
        }

        var bootstrapActorId = ResolveBootstrapActorId(config, userByKey);
        foreach (var userConfig in config.Users.Prepend(config.BootstrapActor).Where(x => x is not null).Cast<SeedUserConfig>())
        {
            if (!userByKey.TryGetValue(userConfig.Key, out var user))
            {
                continue;
            }

            foreach (var membership in userConfig.Memberships)
            {
                await CaptureAsync(result, "membership", $"{userConfig.Key}.{membership.TenantKey ?? membership.TenantId?.ToString("D")}", async () =>
                {
                    await EnsureMembershipAsync(
                            userConfig,
                            user,
                            membership,
                            tenantByKey,
                            roleByKey,
                            bootstrapActorId ?? user.UserId,
                            options,
                            result,
                            ct)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
        }

        return result;
    }

    private async Task<IdentityTenant?> EnsureTenantAsync(
        SeedTenantConfig config,
        SeedBehavior behavior,
        IdentitySeedRunOptions options,
        IdentitySeedResult result,
        CancellationToken ct)
    {
        var existing = await _tenants.FindByIdAsync(config.TenantId, ct).ConfigureAwait(false);
        if (existing is null)
        {
            if (!behavior.CreateMissing)
            {
                AddSkip(result, "tenant", config.Key, "Tenant is missing and createMissing is false.");
                return null;
            }

            Add(result, "tenant", config.Key, "create", $"Create tenant '{config.Name}' ({config.TenantId:D}).");
            return options.Apply
                ? await _tenants.CreateAsync(config.Name, config.TenantId, ct: ct).ConfigureAwait(false)
                : new IdentityTenant(config.TenantId, config.Name, IdentityTenant.NormalizeName(config.Name), IdentityTenantStatuses.Active);
        }

        if (!string.Equals(existing.Name, config.Name, StringComparison.Ordinal))
        {
            if (!behavior.UpdateExisting)
            {
                AddSkip(result, "tenant", config.Key, "Tenant exists with a different name and updateExisting is false.");
                return existing;
            }

            Add(result, "tenant", config.Key, "update", $"Rename tenant from '{existing.Name}' to '{config.Name}'.");
            return options.Apply
                ? await _tenants.UpdateAsync(existing with
                    {
                        Name = config.Name,
                        NormalizedName = IdentityTenant.NormalizeName(config.Name)
                    }, ct: ct).ConfigureAwait(false)
                : existing with { Name = config.Name, NormalizedName = IdentityTenant.NormalizeName(config.Name) };
        }

        AddUnchanged(result, "tenant", config.Key, $"Tenant '{config.Name}' already exists.");
        return existing;
    }

    private async Task<TenantRole?> EnsureRoleAsync(
        SeedTenantConfig tenant,
        SeedRoleConfig config,
        SeedBehavior behavior,
        IdentitySeedRunOptions options,
        IdentitySeedResult result,
        CancellationToken ct)
    {
        var roles = await _roles.GetRolesAsync(tenant.TenantId, ct).ConfigureAwait(false);
        var existing = roles.FirstOrDefault(x => string.Equals(x.Name, config.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            if (!behavior.CreateMissing)
            {
                AddSkip(result, "role", $"{tenant.Key}.{config.Key}", "Role is missing and createMissing is false.");
                return null;
            }

            Add(result, "role", $"{tenant.Key}.{config.Key}", "create", $"Create role '{config.Name}'.");
            return options.Apply
                ? await _roles.CreateRoleAsync(tenant.TenantId, config.Name, ct, config.Description).ConfigureAwait(false)
                : new TenantRole(tenant.TenantId, Guid.NewGuid(), config.Name, false, true, DateTimeOffset.UtcNow, Description: config.Description);
        }

        if (!string.Equals(existing.Description, config.Description, StringComparison.Ordinal))
        {
            if (!behavior.UpdateExisting)
            {
                AddSkip(result, "role", $"{tenant.Key}.{config.Key}", "Role exists with a different description and updateExisting is false.");
                return existing;
            }

            Add(result, "role", $"{tenant.Key}.{config.Key}", "update", $"Update description for role '{config.Name}'.");
            return options.Apply
                ? await _roles.UpdateRoleAsync(tenant.TenantId, existing.RoleId, config.Name, ct, config.Description).ConfigureAwait(false)
                : existing with { Description = config.Description };
        }

        AddUnchanged(result, "role", $"{tenant.Key}.{config.Key}", $"Role '{config.Name}' already exists.");
        return existing;
    }

    private async Task EnsurePermissionMapAsync(
        SeedTenantConfig tenant,
        SeedPermissionMappingConfig config,
        Dictionary<(string TenantKey, string RoleKey), TenantRole> roleByKey,
        IdentitySeedRunOptions options,
        IdentitySeedResult result,
        CancellationToken ct)
    {
        var key = PermissionMapKey(tenant, config);
        var roleIds = ResolveRoleIds(tenant.Key, config.RoleKeys, config.RoleIds, roleByKey);
        var roleNames = ResolveRoleNames(tenant.Key, config.RoleKeys, config.RoleNames, roleByKey);

        var existing = (await _permissionMaps.ListMappingsAsync(tenant.TenantId, ct).ConfigureAwait(false))
            .FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(config.PermissionName)
                    ? string.Equals(x.PermissionName, config.PermissionName, StringComparison.OrdinalIgnoreCase)
                    : x.PermissionId == config.PermissionId);

        if (existing is not null &&
            SetEquals(existing.RoleIds, roleIds) &&
            SetEquals(existing.RoleNames, roleNames))
        {
            AddUnchanged(result, "permission-map", key, "Permission mapping already matches.");
            return;
        }

        Add(result, "permission-map", key, existing is null ? "create" : "update", "Upsert permission role mapping.");
        if (!options.Apply)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(config.PermissionName))
        {
            await _permissionMaps.UpsertByPermissionNameAsync(
                    tenant.TenantId,
                    config.PermissionName,
                    new UpsertPermissionRoleMapRequest
                    {
                        PermissionName = config.PermissionName,
                        RoleIds = roleIds,
                        RoleNames = roleNames
                    },
                    ct)
                .ConfigureAwait(false);
            return;
        }

        if (!config.PermissionId.HasValue || config.PermissionId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Permission mapping requires permissionName or permissionId.");
        }

        await _permissionMaps.UpsertByPermissionIdAsync(
                tenant.TenantId,
                config.PermissionId.Value,
                new UpsertPermissionRoleMapRequest
                {
                    PermissionId = config.PermissionId,
                    RoleIds = roleIds,
                    RoleNames = roleNames
                },
                ct)
            .ConfigureAwait(false);
    }

    private async Task<IdentityUser?> EnsureUserAsync(
        SeedUserConfig config,
        SeedBehavior behavior,
        IdentitySeedRunOptions options,
        IdentitySeedResult result,
        CancellationToken ct)
    {
        var existing = await FindUserAsync(config, ct).ConfigureAwait(false);
        if (existing is null)
        {
            if (!behavior.CreateMissing)
            {
                AddSkip(result, "user", config.Key, "User is missing and createMissing is false.");
                return null;
            }

            Add(result, "user", config.Key, "create", $"Create user '{config.EffectiveEmail ?? config.PhoneNumber}'.");
            if (!options.Apply)
            {
                return new IdentityUser(
                    config.UserId ?? Guid.NewGuid(),
                    config.EffectiveEmail,
                    config.EmailConfirmed == true,
                    config.PhoneNumber,
                    config.PhoneConfirmed == true,
                    config.TwoFactor?.Enabled == true,
                    config.TwoFactor?.PreferredMethod);
            }

            var created = await _users.CreateAsync(
                    new RegisterUserRequest(
                        config.EffectiveEmail,
                        config.PhoneNumber,
                        Password: string.Empty),
                    ct)
                .ConfigureAwait(false);

            if (!created.Succeeded || created.User is null)
            {
                throw new InvalidOperationException("User creation failed: " + string.Join("; ", created.Errors.SelectMany(x => x.Value.Select(v => $"{x.Key}: {v}"))));
            }

            existing = created.User;
        }
        else
        {
            if (config.UserId.HasValue && existing.UserId != config.UserId.Value)
            {
                throw new InvalidOperationException($"User id assertion failed. Config expected {config.UserId:D}, store returned {existing.UserId:D}.");
            }

            AddUnchanged(result, "user", config.Key, "User already exists.");
        }

        if (options.Apply && behavior.UpdateExisting)
        {
            await ApplyUserMutableSettingsAsync(config, existing, result, ct).ConfigureAwait(false);
            existing = await _users.FindByIdAsync(existing.UserId, ct).ConfigureAwait(false) ?? existing;
        }
        else
        {
            AddPlannedUserMutableSettings(config, existing, result, options.Apply, behavior.UpdateExisting);
        }

        return existing;
    }

    private async Task ApplyUserMutableSettingsAsync(
        SeedUserConfig config,
        IdentityUser existing,
        IdentitySeedResult result,
        CancellationToken ct)
    {
        var password = config.ResolvePassword();
        if (!string.IsNullOrWhiteSpace(password))
        {
            await _users.SetPasswordAsync(existing.UserId, password, ct).ConfigureAwait(false);
            Add(result, "user-password", config.Key, "update", "Set password from seed configuration.");
        }

        if (config.EmailConfirmed.HasValue && existing.EmailConfirmed != config.EmailConfirmed.Value)
        {
            await _users.SetEmailConfirmedAsync(existing.UserId, config.EmailConfirmed.Value, ct).ConfigureAwait(false);
            Add(result, "user-email", config.Key, "update", $"Set emailConfirmed={config.EmailConfirmed.Value}.");
        }

        if (config.PhoneConfirmed.HasValue && existing.PhoneConfirmed != config.PhoneConfirmed.Value)
        {
            await _users.SetPhoneConfirmedAsync(existing.UserId, config.PhoneConfirmed.Value, ct).ConfigureAwait(false);
            Add(result, "user-phone", config.Key, "update", $"Set phoneConfirmed={config.PhoneConfirmed.Value}.");
        }

        if (config.TwoFactor is not null &&
            (existing.TwoFactorEnabled != config.TwoFactor.Enabled ||
             !string.Equals(existing.PreferredTwoFactorMethod, config.TwoFactor.PreferredMethod, StringComparison.OrdinalIgnoreCase)))
        {
            await _users.SetTwoFactorAsync(existing.UserId, config.TwoFactor.Enabled, config.TwoFactor.PreferredMethod, ct)
                .ConfigureAwait(false);
            Add(result, "user-2fa", config.Key, "update", $"Set twoFactor.enabled={config.TwoFactor.Enabled}.");
        }
    }

    private static void AddPlannedUserMutableSettings(
        SeedUserConfig config,
        IdentityUser existing,
        IdentitySeedResult result,
        bool apply,
        bool updateExisting)
    {
        if (apply || !updateExisting)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(config.ResolvePassword()))
        {
            Add(result, "user-password", config.Key, "update", "Would set password from seed configuration.");
        }

        if (config.EmailConfirmed.HasValue && existing.EmailConfirmed != config.EmailConfirmed.Value)
        {
            Add(result, "user-email", config.Key, "update", $"Would set emailConfirmed={config.EmailConfirmed.Value}.");
        }

        if (config.PhoneConfirmed.HasValue && existing.PhoneConfirmed != config.PhoneConfirmed.Value)
        {
            Add(result, "user-phone", config.Key, "update", $"Would set phoneConfirmed={config.PhoneConfirmed.Value}.");
        }

        if (config.TwoFactor is not null &&
            (existing.TwoFactorEnabled != config.TwoFactor.Enabled ||
             !string.Equals(existing.PreferredTwoFactorMethod, config.TwoFactor.PreferredMethod, StringComparison.OrdinalIgnoreCase)))
        {
            Add(result, "user-2fa", config.Key, "update", $"Would set twoFactor.enabled={config.TwoFactor.Enabled}.");
        }
    }

    private async Task EnsureMembershipAsync(
        SeedUserConfig userConfig,
        IdentityUser user,
        SeedMembershipConfig membership,
        Dictionary<string, IdentityTenant> tenantByKey,
        Dictionary<(string TenantKey, string RoleKey), TenantRole> roleByKey,
        Guid actorUserId,
        IdentitySeedRunOptions options,
        IdentitySeedResult result,
        CancellationToken ct)
    {
        var tenant = ResolveTenant(membership, tenantByKey);
        var roleIds = ResolveRoleIds(tenant.Key, membership.RoleKeys, membership.RoleIds, roleByKey);
        var roleNames = ResolveRoleNames(tenant.Key, membership.RoleKeys, membership.RoleNames, roleByKey);
        var existingRoles = await _roles.GetRolesForUserAsync(tenant.TenantId, user.UserId, ct).ConfigureAwait(false);

        if (SetContainsAll(existingRoles.Select(x => x.RoleId), roleIds) &&
            SetContainsAll(existingRoles.Select(x => x.Name), roleNames))
        {
            AddUnchanged(result, "membership", $"{userConfig.Key}.{tenant.Key}", "Membership roles already match requested grants.");
        }
        else
        {
            Add(result, "membership", $"{userConfig.Key}.{tenant.Key}", "update", "Ensure tenant membership and role grants.");
            if (options.Apply)
            {
                await _roles.EnsureTenantMembershipAndGrantRolesAsync(
                        new TenantMembershipRoleBootstrapRequest(
                            tenant.TenantId,
                            user.UserId,
                            TenantName: tenant.Name,
                            RoleIds: roleIds,
                            RoleNames: roleNames,
                            SetAsDefault: membership.SetAsDefaultTenant,
                            UserEmail: user.Email,
                            UserPhoneNumber: user.PhoneNumber),
                        ct)
                    .ConfigureAwait(false);
            }
        }

        foreach (var grant in membership.AccessGrants)
        {
            await EnsureResourceGrantAsync(userConfig, user, tenant, grant, actorUserId, options, result, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task EnsureResourceGrantAsync(
        SeedUserConfig userConfig,
        IdentityUser user,
        SeedTenantRef tenant,
        SeedResourceAccessGrantConfig grant,
        Guid actorUserId,
        IdentitySeedRunOptions options,
        IdentitySeedResult result,
        CancellationToken ct)
    {
        var subject = new AccessSubject(IBeam.AccessControl.AccessSubjectTypes.User, user.UserId.ToString("D"));
        var existing = await _resourceAccess.ListGrantsAsync(
                tenant.TenantId,
                grant.ResourceType,
                grant.ResourceId,
                subject,
                ct,
                includeInactive: false)
            .ConfigureAwait(false);

        if (existing.Any(x =>
                string.Equals(x.AccessLevel, grant.AccessLevel, StringComparison.OrdinalIgnoreCase) &&
                x.ExpiresUtc == grant.ExpiresUtc))
        {
            AddUnchanged(result, "access-grant", $"{userConfig.Key}.{tenant.Key}.{grant.ResourceType}.{grant.ResourceId}", "Access grant already exists.");
            return;
        }

        Add(result, "access-grant", $"{userConfig.Key}.{tenant.Key}.{grant.ResourceType}.{grant.ResourceId}", "create", "Grant resource access.");
        if (!options.Apply)
        {
            return;
        }

        await _resourceAccess.GrantAccessAsync(
                tenant.TenantId,
                new GrantResourceAccessRequest
                {
                    ResourceType = grant.ResourceType,
                    ResourceId = grant.ResourceId,
                    AccessLevel = grant.AccessLevel,
                    ExpiresUtc = grant.ExpiresUtc,
                    Subject = subject,
                    Metadata = grant.Metadata
                },
                actorUserId,
                ct)
            .ConfigureAwait(false);
    }

    private async Task<IdentityUser?> FindUserAsync(SeedUserConfig config, CancellationToken ct)
    {
        if (config.UserId.HasValue)
        {
            var byId = await _users.FindByIdAsync(config.UserId.Value, ct).ConfigureAwait(false);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(config.EffectiveEmail))
        {
            var byEmail = await _users.FindByEmailAsync(config.EffectiveEmail, ct).ConfigureAwait(false);
            if (byEmail is not null)
            {
                return byEmail;
            }
        }

        return string.IsNullOrWhiteSpace(config.PhoneNumber)
            ? null
            : await _users.FindByPhoneAsync(config.PhoneNumber, ct).ConfigureAwait(false);
    }

    private static Guid? ResolveBootstrapActorId(
        SeedIdentityConfig config,
        IReadOnlyDictionary<string, IdentityUser> userByKey)
    {
        if (config.BootstrapActor is null)
        {
            return null;
        }

        return userByKey.TryGetValue(config.BootstrapActor.Key, out var user) ? user.UserId : config.BootstrapActor.UserId;
    }

    private static List<Guid> ResolveRoleIds(
        string tenantKey,
        IReadOnlyList<string> roleKeys,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyDictionary<(string TenantKey, string RoleKey), TenantRole> roleByKey)
    {
        var values = roleIds.Where(x => x != Guid.Empty).ToList();
        foreach (var roleKey in roleKeys.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!roleByKey.TryGetValue((tenantKey, roleKey.Trim()), out var role))
            {
                throw new InvalidOperationException($"Role key '{tenantKey}.{roleKey}' was not found.");
            }

            values.Add(role.RoleId);
        }

        return values.Distinct().ToList();
    }

    private static List<string> ResolveRoleNames(
        string tenantKey,
        IReadOnlyList<string> roleKeys,
        IReadOnlyList<string> roleNames,
        IReadOnlyDictionary<(string TenantKey, string RoleKey), TenantRole> roleByKey)
    {
        var values = roleNames.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        foreach (var roleKey in roleKeys.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!roleByKey.TryGetValue((tenantKey, roleKey.Trim()), out var role))
            {
                throw new InvalidOperationException($"Role key '{tenantKey}.{roleKey}' was not found.");
            }

            values.Add(role.Name);
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static SeedTenantRef ResolveTenant(
        SeedMembershipConfig membership,
        IReadOnlyDictionary<string, IdentityTenant> tenantByKey)
    {
        if (!string.IsNullOrWhiteSpace(membership.TenantKey))
        {
            if (!tenantByKey.TryGetValue(membership.TenantKey.Trim(), out var tenant))
            {
                throw new InvalidOperationException($"Tenant key '{membership.TenantKey}' was not found.");
            }

            return new SeedTenantRef(membership.TenantKey.Trim(), tenant.TenantId, tenant.Name);
        }

        if (membership.TenantId.HasValue && membership.TenantId.Value != Guid.Empty)
        {
            var tenant = tenantByKey.FirstOrDefault(x => x.Value.TenantId == membership.TenantId.Value);
            if (string.IsNullOrWhiteSpace(tenant.Key))
            {
                throw new InvalidOperationException($"Tenant id '{membership.TenantId:D}' was not found in the seed tenants.");
            }

            return new SeedTenantRef(tenant.Key, tenant.Value.TenantId, tenant.Value.Name);
        }

        throw new InvalidOperationException("Membership requires tenantKey or tenantId.");
    }

    private static string PermissionMapKey(SeedTenantConfig tenant, SeedPermissionMappingConfig map)
        => $"{tenant.Key}.{map.PermissionName ?? map.PermissionId?.ToString("D") ?? "<missing>"}";

    private static bool SetEquals(IEnumerable<Guid> first, IEnumerable<Guid> second)
        => first.Where(x => x != Guid.Empty).ToHashSet().SetEquals(second.Where(x => x != Guid.Empty));

    private static bool SetEquals(IEnumerable<string> first, IEnumerable<string> second)
        => first.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(second.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static bool SetContainsAll(IEnumerable<Guid> existing, IEnumerable<Guid> requested)
    {
        var set = existing.Where(x => x != Guid.Empty).ToHashSet();
        return requested.Where(x => x != Guid.Empty).All(set.Contains);
    }

    private static bool SetContainsAll(IEnumerable<string> existing, IEnumerable<string> requested)
    {
        var set = existing.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requested.Where(x => !string.IsNullOrWhiteSpace(x)).All(set.Contains);
    }

    private static async Task CaptureAsync(
        IdentitySeedResult result,
        string entity,
        string key,
        Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Failures.Add(new IdentitySeedFailure(entity, key, ex.Message));
        }
    }

    private static void AddUnchanged(IdentitySeedResult result, string entity, string key, string message)
        => Add(result, entity, key, "unchanged", message);

    private static void AddSkip(IdentitySeedResult result, string entity, string key, string message)
        => Add(result, entity, key, "skip", message);

    private static void Add(IdentitySeedResult result, string entity, string key, string action, string message)
        => result.Changes.Add(new IdentitySeedChange(entity, key, action, message));

    private sealed record SeedTenantRef(string Key, Guid TenantId, string Name);
}
