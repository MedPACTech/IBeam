using System.Security.Claims;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.ApiCredentials;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class ApiCredentialServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("225925cc-995e-4584-a63b-4f2cb4f38f6f");
    private static readonly Guid ApiRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AdminRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public async Task CreateAsync_ReturnsRawKeyOnce_AndStoresHashOnly()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.CreateAsync(
            TenantId,
            new CreateApiCredentialRequest
            {
                DisplayName = "Codex Work Service",
                AgentKey = "codex",
                RoleIds = [ApiRoleId],
                RoleNames = ["api-scope:work"]
            },
            Guid.NewGuid());

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ApiKey));
        Assert.IsTrue(result.Credential.KeyPrefix.StartsWith("ibk_", StringComparison.Ordinal));
        Assert.AreEqual("Codex Work Service", result.Credential.DisplayName);
        CollectionAssert.Contains(result.Credential.RoleNames.ToList(), "API");
        CollectionAssert.Contains(result.Credential.RoleNames.ToList(), "api-scope:work");

        var stored = await fixture.Store.GetAsync(TenantId, result.Credential.Id);
        Assert.IsNotNull(stored);
        Assert.AreNotEqual(result.ApiKey, stored.SecretHash);
        Assert.IsTrue(stored.SecretHash.StartsWith("pbkdf2-sha256:v1:", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AuthenticateAsync_ValidKey_EmitsCredentialPrincipal_AndTouchesLastUsed()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(
            TenantId,
            new CreateApiCredentialRequest
            {
                DisplayName = "Marketing App",
                AgentKey = "marketing",
                RoleNames = ["API", "api-scope:email"]
            },
            Guid.NewGuid());

        var result = await fixture.Authenticator.AuthenticateAsync(created.ApiKey, "127.0.0.1");

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Principal);
        Assert.AreEqual("credential", result.Principal.FindFirstValue("api_subject_type"));
        Assert.AreEqual(created.Credential.Id.ToString("D"), result.Principal.FindFirstValue("api_credential_id"));
        Assert.AreEqual(TenantId.ToString("D"), result.Principal.FindFirstValue("tid"));
        Assert.AreEqual("marketing", result.Principal.FindFirstValue("agent_key"));
        CollectionAssert.Contains(result.Principal.FindAll("role").Select(x => x.Value).ToList(), "api-scope:email");

        var touched = await fixture.Store.GetAsync(TenantId, created.Credential.Id);
        Assert.IsNotNull(touched!.LastUsedUtc);
        Assert.AreEqual("127.0.0.1", touched.LastUsedIp);
    }

    [TestMethod]
    public async Task AuthenticateAsync_RevokedKey_Fails()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(
            TenantId,
            new CreateApiCredentialRequest { DisplayName = "Worker", RoleNames = ["API"] },
            Guid.NewGuid());

        await fixture.Service.RevokeAsync(TenantId, created.Credential.Id, Guid.NewGuid(), "rotated");

        var result = await fixture.Authenticator.AuthenticateAsync(created.ApiKey);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("revoked", result.FailureReason);
    }

    [TestMethod]
    public async Task AuthenticateAsync_InvalidSecret_Fails()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(
            TenantId,
            new CreateApiCredentialRequest { DisplayName = "Worker", RoleNames = ["API"] },
            Guid.NewGuid());

        var parts = created.ApiKey.Split('_');
        parts[^1] = "invalidsecret";
        var result = await fixture.Authenticator.AuthenticateAsync(string.Join('_', parts));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("invalid_hash", result.FailureReason);
    }

    [TestMethod]
    public async Task UpdateRolesAsync_AcceptsApiCredentialCatalogScopeNames()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(
            TenantId,
            new CreateApiCredentialRequest { DisplayName = "Hubbsly Agent", RoleNames = ["API"] },
            Guid.NewGuid());

        var updated = await fixture.Service.UpdateRolesAsync(
            TenantId,
            created.Credential.Id,
            new UpdateApiCredentialRolesRequest
            {
                RoleNames = ["API", "tool:mcp", "api-scope:work"]
            });

        CollectionAssert.AreEquivalent(
            new[] { "API", "api-scope:work", "tool:mcp" },
            updated.RoleNames.ToArray());
        Assert.IsFalse(updated.RoleIds.Any());
    }

    [TestMethod]
    public void TryParse_PreservesLeadingUnderscoreSecret()
    {
        var generator = new ApiCredentialKeyGenerator(Options.Create(new ApiCredentialOptions()));
        var credentialId = Guid.NewGuid();
        var raw = $"ibk_{TenantId:N}_{credentialId:N}__secret-with-leading-underscore";

        var parsed = generator.TryParse(raw, out var key);

        Assert.IsTrue(parsed);
        Assert.AreEqual(TenantId, key.TenantId);
        Assert.AreEqual(credentialId, key.CredentialId);
        Assert.AreEqual("_secret-with-leading-underscore", key.Secret);
    }

    [TestMethod]
    public void CreateKey_UsesConfiguredKeyPrefix()
    {
        var generator = new ApiCredentialKeyGenerator(Options.Create(new ApiCredentialOptions
        {
            KeyPrefix = "hbk"
        }));

        var credentialId = Guid.NewGuid();
        var created = generator.CreateKey(TenantId, credentialId);

        Assert.IsTrue(created.RawKey.StartsWith($"hbk_{TenantId:N}_{credentialId:N}_", StringComparison.Ordinal));
        Assert.IsTrue(created.KeyPrefix.StartsWith("hbk_", StringComparison.Ordinal));
        Assert.AreEqual("hbk", created.ParsedKey.Prefix);
    }

    [TestMethod]
    public async Task CreateAsync_DeniesHumanManagementRoles()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsExactlyAsync<IdentityValidationException>(() =>
            fixture.Service.CreateAsync(
                TenantId,
                new CreateApiCredentialRequest
                {
                    DisplayName = "Unsafe",
                    RoleIds = [AdminRoleId]
                },
                Guid.NewGuid()));
    }

    [TestMethod]
    public async Task RoleCatalogProvider_ReturnsBuiltIns_AndConfiguredEntries()
    {
        var provider = new ApiCredentialRoleCatalogProvider(Options.Create(new ApiCredentialOptions
        {
            RoleCatalog =
            [
                new ApiCredentialRoleCatalogEntryOptions
                {
                    Name = "api-scope:calendar",
                    DisplayName = "Calendar",
                    Description = "Allows access to Calendar API and MCP tools.",
                    Category = "module"
                }
            ]
        }));

        var entries = await provider.ListAsync();
        var names = entries.Select(x => x.Name).ToList();

        CollectionAssert.Contains(names, "API");
        CollectionAssert.Contains(names, "tool:mcp");
        CollectionAssert.Contains(names, "api-scope:*");
        CollectionAssert.Contains(names, "api-scope:work");
        CollectionAssert.Contains(names, "api-scope:contacts");
        CollectionAssert.Contains(names, "api-scope:money");
        CollectionAssert.Contains(names, "agent:*");
        CollectionAssert.Contains(names, "api-scope:calendar");

        var custom = entries.Single(x => x.Name == "api-scope:calendar");
        Assert.IsFalse(custom.IsBuiltIn);
        Assert.IsTrue(custom.IsAssignable);
        Assert.AreEqual("module", custom.Category);

        var pattern = entries.Single(x => x.Name == "api-scope:*");
        Assert.IsTrue(pattern.IsBuiltIn);
        Assert.IsTrue(pattern.IsPattern);
        Assert.IsFalse(pattern.IsAssignable);
    }

    private static Fixture CreateFixture()
    {
        var options = Options.Create(new ApiCredentialOptions());
        var roleStore = new FakeTenantRoleStore();
        var store = new InMemoryApiCredentialStore();
        var keyGenerator = new ApiCredentialKeyGenerator(options);
        var hasher = new ApiCredentialSecretHasher(options);
        var validator = new ApiCredentialRoleAssignmentValidator(roleStore, options);
        var principalFactory = new ApiCredentialPrincipalFactory(options);
        var service = new ApiCredentialService(store, roleStore, validator, keyGenerator, hasher);
        var authenticator = new ApiCredentialAuthenticator(store, keyGenerator, hasher, principalFactory);
        return new Fixture(store, service, authenticator);
    }

    private sealed record Fixture(
        InMemoryApiCredentialStore Store,
        ApiCredentialService Service,
        ApiCredentialAuthenticator Authenticator);

    private sealed class InMemoryApiCredentialStore : IApiCredentialStore
    {
        private readonly Dictionary<(Guid TenantId, Guid CredentialId), ApiCredentialRecord> _records = [];

        public Task<ApiCredentialRecord> CreateAsync(ApiCredentialRecord credential, CancellationToken ct = default)
        {
            _records[(credential.TenantId, credential.CredentialId)] = credential;
            return Task.FromResult(credential);
        }

        public Task<IReadOnlyList<ApiCredentialRecord>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ApiCredentialRecord>>(
                _records.Values.Where(x => x.TenantId == tenantId && !x.IsDeleted).ToList());

        public Task<ApiCredentialRecord?> GetAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default)
        {
            _records.TryGetValue((tenantId, credentialId), out var record);
            return Task.FromResult(record);
        }

        public Task<ApiCredentialRecord> UpdateRolesAsync(
            Guid tenantId,
            Guid credentialId,
            IReadOnlyList<Guid> roleIds,
            IReadOnlyList<string> roleNames,
            CancellationToken ct = default)
        {
            var updated = _records[(tenantId, credentialId)] with
            {
                RoleIds = roleIds,
                RoleNames = roleNames
            };
            _records[(tenantId, credentialId)] = updated;
            return Task.FromResult(updated);
        }

        public Task<ApiCredentialRecord> RevokeAsync(
            Guid tenantId,
            Guid credentialId,
            Guid? revokedByUserId,
            string? reason,
            CancellationToken ct = default)
        {
            var revoked = _records[(tenantId, credentialId)] with
            {
                RevokedUtc = DateTimeOffset.UtcNow,
                RevokedByUserId = revokedByUserId,
                RevocationReason = reason
            };
            _records[(tenantId, credentialId)] = revoked;
            return Task.FromResult(revoked);
        }

        public Task TouchLastUsedAsync(
            Guid tenantId,
            Guid credentialId,
            DateTimeOffset usedUtc,
            string? ipAddress,
            CancellationToken ct = default)
        {
            var touched = _records[(tenantId, credentialId)] with
            {
                LastUsedUtc = usedUtc,
                LastUsedIp = ipAddress
            };
            _records[(tenantId, credentialId)] = touched;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTenantRoleStore : ITenantRoleStore
    {
        public Task<IReadOnlyList<TenantRole>> GetRolesAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantRole>>(
            [
                new TenantRole(tenantId, ApiRoleId, "API", false, true, DateTimeOffset.UtcNow),
                new TenantRole(tenantId, AdminRoleId, "Admin", true, true, DateTimeOffset.UtcNow)
            ]);

        public Task<TenantRole?> GetRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
            => Task.FromResult<TenantRole?>(null);

        public Task<TenantRole> CreateRoleAsync(Guid tenantId, string name, bool isSystem = false, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TenantRole> UpdateRoleAsync(Guid tenantId, Guid roleId, string name, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<UserTenantRoleAssignment> GrantRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<UserTenantRoleAssignment> EnsureTenantMembershipAndGrantRolesAsync(TenantMembershipRoleBootstrapRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<UserTenantRoleAssignment> RevokeRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantRole>> GetRolesForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task EnsureDefaultRolesAsync(Guid tenantId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
