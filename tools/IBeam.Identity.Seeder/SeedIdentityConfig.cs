using System.Text.Json;
using System.Text.Json.Serialization;

namespace IBeam.Identity.Seeder;

internal sealed class SeedIdentityConfig
{
    public int Version { get; set; } = 1;
    public SeedBehavior Behavior { get; set; } = new();
    public SeedUserConfig? BootstrapActor { get; set; }
    public List<SeedTenantConfig> Tenants { get; set; } = [];
    public List<SeedUserConfig> Users { get; set; } = [];

    public static async Task<SeedIdentityConfig> LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Seed config file was not found.", path);
        }

        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<SeedIdentityConfig>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new JsonStringEnumConverter() }
            },
            ct);

        return config ?? throw new InvalidOperationException("Seed config was empty.");
    }

    public void Validate()
    {
        if (Version <= 0)
        {
            throw new InvalidOperationException("version must be greater than zero.");
        }

        EnsureUnique(Tenants.Select(x => x.Key), "tenant key");
        EnsureUnique(Users.Select(x => x.Key), "user key");

        foreach (var tenant in Tenants)
        {
            tenant.Validate();
        }

        foreach (var user in Users.Prepend(BootstrapActor).Where(x => x is not null).Cast<SeedUserConfig>())
        {
            user.Validate();
        }
    }

    private static void EnsureUnique(IEnumerable<string?> values, string label)
    {
        var duplicates = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate {label}: {string.Join(", ", duplicates)}.");
        }
    }
}

internal sealed class SeedBehavior
{
    public bool CreateMissing { get; set; } = true;
    public bool UpdateExisting { get; set; } = true;
}

internal sealed class SeedTenantConfig
{
    public string Key { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<SeedRoleConfig> Roles { get; set; } = [];
    public List<SeedPermissionMappingConfig> PermissionMappings { get; set; } = [];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new InvalidOperationException("Each tenant requires a key.");
        }

        if (TenantId == Guid.Empty)
        {
            throw new InvalidOperationException($"Tenant '{Key}' requires tenantId.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException($"Tenant '{Key}' requires name.");
        }

        var duplicateRoles = Roles
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();

        if (duplicateRoles.Count > 0)
        {
            throw new InvalidOperationException($"Tenant '{Key}' has duplicate role key(s): {string.Join(", ", duplicateRoles)}.");
        }

        foreach (var role in Roles)
        {
            role.Validate(Key);
        }
    }
}

internal sealed class SeedRoleConfig
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public void Validate(string tenantKey)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new InvalidOperationException($"Tenant '{tenantKey}' has a role without a key.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException($"Tenant '{tenantKey}' role '{Key}' requires name.");
        }
    }
}

internal sealed class SeedPermissionMappingConfig
{
    public string? PermissionName { get; set; }
    public Guid? PermissionId { get; set; }
    public List<string> RoleKeys { get; set; } = [];
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
}

internal sealed class SeedUserConfig
{
    public string Key { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PasswordEnv { get; set; }
    public string? Password { get; set; }
    public bool? EmailConfirmed { get; set; }
    public bool? PhoneConfirmed { get; set; }
    public SeedTwoFactorConfig? TwoFactor { get; set; }
    public List<SeedMembershipConfig> Memberships { get; set; } = [];

    public string? EffectiveEmail =>
        !string.IsNullOrWhiteSpace(Email)
            ? Email
            : UserName?.Contains('@', StringComparison.Ordinal) == true
                ? UserName
                : null;

    public string? ResolvePassword()
    {
        if (!string.IsNullOrWhiteSpace(PasswordEnv))
        {
            return Environment.GetEnvironmentVariable(PasswordEnv.Trim());
        }

        return Password;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new InvalidOperationException("Each user requires a key.");
        }

        if (string.IsNullOrWhiteSpace(EffectiveEmail) && string.IsNullOrWhiteSpace(PhoneNumber))
        {
            throw new InvalidOperationException($"User '{Key}' requires email, email-like userName, or phoneNumber.");
        }
    }
}

internal sealed class SeedTwoFactorConfig
{
    public bool Enabled { get; set; }
    public string? PreferredMethod { get; set; }
}

internal sealed class SeedMembershipConfig
{
    public string? TenantKey { get; set; }
    public Guid? TenantId { get; set; }
    public List<string> RoleKeys { get; set; } = [];
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
    public bool SetAsDefaultTenant { get; set; }
    public List<SeedResourceAccessGrantConfig> AccessGrants { get; set; } = [];
}

internal sealed class SeedResourceAccessGrantConfig
{
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string AccessLevel { get; set; } = "view";
    public DateTimeOffset? ExpiresUtc { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}
