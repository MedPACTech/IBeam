using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Invites;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Tests.Management.Applications;
using IBeam.Tests.Management.CustomerIdentity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.PackageValidation;

// Compiled only in the isolated package consumer. Fixture transports are scoped to TestServer;
// production origin/TLS rules and real customer request/refresh tests remain unchanged.
[TestClass]
public sealed class EnterpriseWorkflowPackageTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [TestMethod]
    public async Task OneSupportApiDrivesTwoIsolatedEnterpriseTargetsThroughNativeVerifiedAcceptanceAndLifecycle()
    {
        await using var portal = await PortalHttpFixture.CreateAsync(customerDirectories: true, customerOnboarding: true,
            customerProvisioning: true, customerMemberships: true, customerTenantStatus: true, audit: true);
        var grants = ManagementCustomerIdentityOperations.All.Where(ManagementCustomerIdentityPermissionPolicy.IsAssignable).ToArray();
        var apps = new Dictionary<string, (Guid App, DelegationFixture Target, OnboardingFixture Data, MembershipStorageFixture Storage)>();
        var storages = new List<MembershipStorageFixture>();
        var customer = Guid.NewGuid(); var selfTenant = Guid.NewGuid(); var member = Guid.NewGuid();
        try {
            foreach (var key in new[] { "qurvia", "bindry" }) {
                var id = await portal.Directories.AddApp(portal, key, grants, includeOnboarding: true, includeProvisioning: true, includeMemberships: true, includeTenantStatus: true);
                var storage = await MembershipStorageFixture.Create(key == "bindry" ? Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_SEPARATE_STORAGE") : null);
                storages.Add(storage);
                var data = new OnboardingFixture(); var provisioning = new ProvisioningFixture(data);
                await storage.Tenants.CreateAsync(new(selfTenant, "Existing self-service workspace"));
                await storage.Roles.EnsureTenantMembershipAndGrantRolesAsync(new(selfTenant, customer, RoleNames: ["Owner"], SetAsDefault: true));
                var users = new Dictionary<Guid, IdentityUser> { [customer] = new(customer, data.Request.InitialAdminEmail, true), [member] = new(member, provisioning.Request.Email, true) };
                data.Users.Setup(x => x.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid user, CancellationToken _) => users.GetValueOrDefault(user));
                data.Users.Setup(x => x.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string email, CancellationToken _) => users.Values.SingleOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)));
                var profile = portal.Directories.Targets.Values.Single(x => x.App == id).Info;
                var target = await DelegationFixture.Start(new AzureTableManagementCustomerTenantReadRepository(storage.TenantTable),
                    new AzureTableManagementCustomerUserReadRepository(storage.Primary), tenantStore: storage.Tenants,
                    onboarding: data, provisioning: provisioning, memberships: storage, tenantStatus: true,
                    organization: portal.Data.Access.Org, supportUser: portal.Data.Access.Reader, application: id,
                    identityRealm: profile.IdentityRealmId, applicationKey: key, environment: profile.Environment,
                    createdHook: async tenant => { await storage.Roles.EnsureDefaultRolesAsync(tenant.TenantId); await storage.Roles.CreateRoleAsync(tenant.TenantId, "Member"); },
                    membershipPolicies: new Dictionary<string, string[]> { ["member-administrator"] = ["Member", "Owner"] });
                apps.Add(key, (id, target, data, storage));
            }
            var forwarded = "no forward call";
            portal.Directories.Forward = async (request, operation, ct) => {
                var key = request.RequestUri!.Host.Split('.')[1]; var host = apps[key].Target;
                var bytes = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(ct);
                var keyHeader = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : "unused-get-key";
                var version = request.Headers.TryGetValues("If-Match", out var versions) ? versions.Single() : null;
                forwarded = operation + " " + request.Method.Method + " " + request.RequestUri.PathAndQuery;
                try {
                var proof = await host.Issue(operation, request.Method.Method, request.RequestUri.PathAndQuery, bytes, keyHeader, version);
                var reply = await host.Send(proof, request.Method.Method, request.RequestUri.PathAndQuery, bytes.Length == 0 ? null : bytes, keyHeader, version);
                forwarded += " " + (int)reply.StatusCode + " " + await reply.Content.ReadAsStringAsync(ct); return reply;
                } catch(Exception ex) { forwarded += " " + ex.GetType().Name + " " + ex.StackTrace; throw; }
            };
            var created = new List<Guid>();
            foreach (var entry in apps.Values) {
                var root = $"/api/management/applications/{entry.App}";
                var setup = await Write(portal, HttpMethod.Post, root + "/enterprise-onboardings", entry.Data.Request, "enterprise-full-flow-01");
                Assert.AreEqual(HttpStatusCode.Accepted, setup.Status);
                var operation = setup.Data.GetProperty("operation").Deserialize<ManagementIdentityOperationDto>(Json)!;
                Assert.IsNotNull(operation, forwarded);
                Assert.IsNotNull(operation.Onboarding, JsonSerializer.Serialize(operation));
                var tenant = operation.Onboarding!.TenantId!.Value; created.Add(tenant);
                Assert.AreEqual("awaiting-acceptance", operation.Onboarding.Status);
                Assert.IsNull(await entry.Storage.Members.GetTenantForUserAsync(customer, tenant));
                await Accept(entry.Data, entry.Storage, customer);
                var tracking = setup.Data.GetProperty("reference").GetString();
                using var completeResponse = await portal.Get(root + "/operations/" + tracking + "?authority=" + ManagementCustomerIdentityOperations.OnboardingCreate);
                Assert.AreEqual(HttpStatusCode.OK, completeResponse.StatusCode, await completeResponse.Content.ReadAsStringAsync());
                // Authoritative target receipt query is also exercised directly; polling never accepts or sends mail.
                var path = "/api/management-identity/operations/" + operation.OperationId;
                using var complete = await entry.Target.Send(await entry.Target.Issue(ManagementCustomerIdentityOperations.OnboardingRead, path: path), path: path);
                using var completeJson = JsonDocument.Parse(await complete.Content.ReadAsStringAsync());
                Assert.IsTrue(completeJson.RootElement.GetProperty("data").GetProperty("onboarding").GetProperty("completed").GetBoolean());
                var user = await Get(portal, root + $"/tenants/{tenant}/users/{customer}"); Assert.AreEqual(customer, user.GetProperty("userId").GetGuid());
                var catalog = await Get(portal, root + $"/tenants/{tenant}/assignable-roles");
                var memberRole = catalog.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Member").GetProperty("roleId").GetGuid();
                var ownerRole = catalog.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Owner").GetProperty("roleId").GetGuid();
                var provision = await Write(portal, HttpMethod.Post, root + $"/tenants/{tenant}/users/provision", new ProvisionManagementCustomerUser {
                    Email = "customer@example.test", PolicyKey = "default", RoleIds = [memberRole] }, "enterprise-member-01");
                Assert.AreEqual(HttpStatusCode.Accepted, provision.Status); await Accept(entry.Data, entry.Storage, member);
                var memberState = await Get(portal, root + $"/tenants/{tenant}/users/{member}");
                Assert.IsTrue(memberState.GetProperty("roleIds").EnumerateArray().Select(x => x.GetGuid()).SequenceEqual([memberRole]));
                Assert.AreEqual(HttpStatusCode.Forbidden, (await Write(portal, HttpMethod.Put, root + $"/tenants/{tenant}/users/{member}/roles",
                    new ChangeManagementCustomerRoles { PolicyKey = "administrator", RoleIds = [ownerRole] }, "enterprise-promote-narrow-policy", memberState.GetProperty("version").GetString())).Status);
                Assert.AreEqual(memberState.GetProperty("version").GetString(), (await Get(portal, root + $"/tenants/{tenant}/users/{member}")).GetProperty("version").GetString());
                Assert.AreEqual(HttpStatusCode.OK, (await Write(portal, HttpMethod.Put, root + $"/tenants/{tenant}/users/{member}/roles",
                    new ChangeManagementCustomerRoles { PolicyKey = "member-administrator", RoleIds = [ownerRole] }, "enterprise-promote-01", memberState.GetProperty("version").GetString())).Status);
                user = await Get(portal, root + $"/tenants/{tenant}/users/{customer}");
                Assert.AreEqual(HttpStatusCode.OK, (await Write(portal, HttpMethod.Patch, root + $"/tenants/{tenant}/users/{customer}/membership",
                    new ChangeManagementCustomerMembership { IsEnabled = false }, "enterprise-disable-01", user.GetProperty("version").GetString())).Status);
                memberState = await Get(portal, root + $"/tenants/{tenant}/users/{member}");
                Assert.AreEqual(HttpStatusCode.Conflict, (await Write(portal, HttpMethod.Patch, root + $"/tenants/{tenant}/users/{member}/membership",
                    new ChangeManagementCustomerMembership { IsEnabled = false }, "enterprise-last-admin-01", memberState.GetProperty("version").GetString())).Status);
                var tenantState = await Get(portal, root + $"/tenants/{tenant}");
                Assert.AreEqual(HttpStatusCode.OK, (await Write(portal, HttpMethod.Post, root + $"/tenants/{tenant}/deactivate", new { },
                    "enterprise-deactivate-01", tenantState.GetProperty("version").GetString())).Status);
                Assert.IsTrue((await entry.Storage.Members.GetTenantForUserAsync(customer, selfTenant))!.IsActive);
                var own = await entry.Storage.Secondary.GetEntityAsync<Azure.Data.Tables.TableEntity>("USR|" + customer, "TEN|" + selfTenant);
                Assert.IsTrue(own.Value.GetBoolean("IsDefault") == true);
            }
            Assert.AreNotEqual(created[0], created[1]);
            var second = apps["bindry"]; portal.Data.Access.Access.Rows.Remove((portal.Data.Access.Org, second.App, portal.Data.Access.Reader));
            using var denied = await portal.Get($"/api/management/applications/{second.App}/tenants"); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            var audit = JsonSerializer.Serialize(portal.Audit.Rows);
            Assert.DoesNotContain("enterprise@example.test", audit); Assert.DoesNotContain("customer@example.test", audit);
            foreach (var entry in apps.Values) Assert.DoesNotContain(entry.Data.LastRequest!.Token, audit);
        } finally { foreach (var entry in apps.Values) { await entry.Target.DisposeAsync(); } foreach (var storage in storages) await storage.DisposeAsync(); }
    }
    private static async Task<JsonElement> Get(PortalHttpFixture portal, string path)
    {
        using var response = await portal.Get(path); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        using var json = JsonDocument.Parse(text); return json.RootElement.GetProperty("data").Clone();
    }
    private static async Task<(HttpStatusCode Status, JsonElement Data)> Write(PortalHttpFixture portal, HttpMethod method, string path, object body, string key, string? version = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) }; request.Headers.Add("Authorization", "Bearer reader");
        request.Headers.Add("Idempotency-Key", key); if(version is not null) request.Headers.TryAddWithoutValidation("If-Match", version);
        using var response = await portal.Client.SendAsync(request); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default);
    }
    private static async Task Accept(OnboardingFixture data, MembershipStorageFixture storage, Guid userId)
    {
        var request = data.LastRequest!; var row = data.InviteRows[request.InviteId];
        data.InviteRows[request.InviteId] = row with { TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token))), RequireVerifiedAcceptance = true, Version = "v1" };
        var roles = new Mock<ITenantRoleService>();
        roles.Setup(x => x.EnsureTenantMembershipAndGrantRolesAsync(It.IsAny<TenantMembershipRoleBootstrapRequest>(), It.IsAny<CancellationToken>()))
            .Returns((TenantMembershipRoleBootstrapRequest r, CancellationToken ct) => storage.Roles.EnsureTenantMembershipAndGrantRolesAsync(r, ct));
        var tenants = new Mock<IIdentityTenantService>(); tenants.Setup(x => x.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken ct) => storage.Tenants.FindByIdAsync(id, ct));
        var tokens = new Mock<ITokenService>(); tokens.Setup(x => x.CreateAccessTokenAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ClaimItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResult("synthetic-customer-token", DateTimeOffset.UtcNow.AddMinutes(1), []));
        var service = new TenantInviteService(storage.Roles, new AcceptanceStore(data), tenants.Object, data.Users.Object, roles.Object,
            Mock.Of<ITenantProvisioningService>(), storage.Members, Mock.Of<IIdentityUserExtensionCoordinator>(), Mock.Of<IIdentityCommunicationSender>(),
            new DefaultTenantInviteUrlBuilder(new DefaultTenantInviteLinkPolicy(Options.Create(new TenantInviteLinkOptions()), NullLogger<DefaultTenantInviteLinkPolicy>.Instance)),
            new DefaultTenantInviteMessageFactory(), Mock.Of<IOtpService>(), Mock.Of<IOtpChallengeStore>(), tokens.Object,
            new NoOpAuthEventPublisher(), Options.Create(new AuthEventOptions()), NullLogger<TenantInviteService>.Instance);
        var accepted = await service.AcceptInviteAsync(new(InviteToken: request.Token, Mode: TenantInviteAcceptModes.ExistingSession), userId);
        Assert.AreEqual(userId, accepted.User.UserId); Assert.IsFalse(accepted.CreatedNewUser);
    }
    private sealed class AcceptanceStore(OnboardingFixture data) : ITenantInviteStore, IManagedTenantInviteStore
    {
        public Task<TenantInviteRecord> CreateAsync(TenantInviteRecord row, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantInviteRecord>> ListForTenantAsync(Guid tenant, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TenantInviteRecord>>(data.InviteRows.Values.Where(x => x.TenantId == tenant).ToArray());
        public Task<TenantInviteRecord?> GetAsync(Guid tenant, Guid id, CancellationToken ct = default) => Task.FromResult(data.InviteRows.TryGetValue(id, out var row) && row.TenantId == tenant ? row : null);
        public Task<TenantInviteRecord?> FindByTokenHashAsync(string hash, CancellationToken ct = default) => Task.FromResult(data.InviteRows.Values.SingleOrDefault(x => x.TokenHash == hash));
        public Task<TenantInviteRecord> UpdateAsync(TenantInviteRecord row, CancellationToken ct = default) { data.InviteRows[row.InviteId] = row; return Task.FromResult(row); }
        public Task<TenantInviteRecord> BeginAcceptanceAsync(Guid tenant, Guid id, string hash, Guid user, CancellationToken ct = default)
        { var row = data.InviteRows[id]; Assert.AreEqual(tenant, row.TenantId); Assert.AreEqual(hash, row.TokenHash); row = row with { AcceptanceId = row.AcceptanceId ?? Guid.NewGuid(), AcceptanceUserId = user }; data.InviteRows[id] = row; return Task.FromResult(row); }
        public Task<TenantInviteRecord> TransitionAsync(ManagedTenantInviteTransition request, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
