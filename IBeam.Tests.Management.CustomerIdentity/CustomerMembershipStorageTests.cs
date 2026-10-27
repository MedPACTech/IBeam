using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Api.DependencyInjection;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Identity.Services.Tenants;
using IBeam.Identity.Services.Tokens;
using IBeam.Services.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerMembershipStorageTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ConcurrentDifferentMemberDisablesCannotRemoveTheFinalAdministrator()
    {
        await using var f = await MembershipStorageFixture.Create();
        await f.Members.ConfigureProtectionAsync(f.Tenant, [f.Owner.RoleId]);
        var snapshot = await f.Members.ReadSnapshotAsync(f.Tenant, 5000);
        var a = snapshot.Members.Single(x => x.UserId == f.AdminA); var b = snapshot.Members.Single(x => x.UserId == f.AdminB);
        var barrier = new SnapshotBarrier(f.Members); var service = f.Administration(barrier);
        async Task<bool> Disable(IdentityTenantMembershipState member) {
            try { await service.SetEnabledAsync(f.Tenant, member.UserId, false, member.Version, Guid.NewGuid(), [f.Owner.RoleId]); return true; }
            catch (Exception ex) when (ex is IdentityConcurrencyException or IdentityValidationException) { return false; }
        }
        var outcomes = await Task.WhenAll(Disable(a), Disable(b)); Assert.AreEqual(1, outcomes.Count(x => x));
        var after = await f.Members.ReadSnapshotAsync(f.Tenant, 5000);
        Assert.AreEqual(1, after.Members.Count(x => x.IsEnabled && x.RoleIds.Contains(f.Owner.RoleId)));
        var last = after.Members.Single(x => x.IsEnabled && x.RoleIds.Contains(f.Owner.RoleId));
        await Assert.ThrowsAsync<IdentityValidationException>(() => f.Administration().SetRolesAsync(f.Tenant, last.UserId, [], last.Version, Guid.NewGuid(), [f.Owner.RoleId]));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task DisablingAndRemovingRolesDoesNotTouchOtherTenantsDefaultsOrRestorePrivilegesOnReenable()
    {
        await using var f = await MembershipStorageFixture.Create();
        var otherBefore = (await f.Secondary.GetEntityAsync<TableEntity>("USR|" + f.AdminA, "TEN|" + f.OtherTenant)).Value.ETag;
        var original = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!; var op = Guid.NewGuid();
        var disabled = await f.Administration().SetEnabledAsync(f.Tenant, f.AdminA, false, original.Version, op, [f.Owner.RoleId]);
        var replay = await f.Administration().SetEnabledAsync(f.Tenant, f.AdminA, false, original.Version, op, [f.Owner.RoleId]);
        Assert.AreEqual(disabled.Version, replay.Version);
        var cleared = await f.Administration().SetRolesAsync(f.Tenant, f.AdminA, [], disabled.Version, Guid.NewGuid(), [f.Owner.RoleId]);
        Assert.IsFalse(cleared.IsEnabled); Assert.HasCount(0, cleared.RoleIds);
        var enabled = await f.Administration().SetEnabledAsync(f.Tenant, f.AdminA, true, cleared.Version, Guid.NewGuid(), [f.Owner.RoleId]);
        Assert.IsTrue(enabled.IsEnabled); Assert.HasCount(0, enabled.RoleIds);
        Assert.AreEqual(otherBefore, (await f.Secondary.GetEntityAsync<TableEntity>("USR|" + f.AdminA, "TEN|" + f.OtherTenant)).Value.ETag);
        var primary = (await f.Primary.GetEntityAsync<TableEntity>("TEN|" + f.Tenant, "USR|" + f.AdminA)).Value;
        Assert.AreEqual("KEEP-CONSUMER-COLUMN", primary.GetString("ConsumerColumn")); Assert.IsFalse(primary.ContainsKey("DisabledAt"));
        var index = (await f.Secondary.GetEntityAsync<TableEntity>("USR|" + f.AdminA, "TEN|" + f.Tenant)).Value;
        Assert.IsTrue(index.GetBoolean("IsDefault"));
        Assert.IsTrue((await f.Members.GetTenantForUserAsync(f.AdminA, f.OtherTenant))!.IsActive);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task NativeBootstrapCannotReactivateDisabledMembersAndLegacyDestructiveWritersFailClosed()
    {
        await using var f = await MembershipStorageFixture.Create();
        var original = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        await f.Administration().SetEnabledAsync(f.Tenant, f.AdminA, false, original.Version, Guid.NewGuid(), [f.Owner.RoleId]);
        await Assert.ThrowsAsync<IdentityValidationException>(() => f.Roles.EnsureTenantMembershipAndGrantRolesAsync(new(f.Tenant, f.AdminA, RoleIds: [f.Owner.RoleId])));
        Assert.IsFalse((await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.IsEnabled);
        await Assert.ThrowsAsync<IdentityValidationException>(() => f.Members.DisableTenantMembershipAsync(f.Tenant, f.AdminB));
        await Assert.ThrowsAsync<IdentityValidationException>(() => f.Roles.RevokeRolesAsync(f.Tenant, f.AdminB, [f.Owner.RoleId]));
        var nativeService = new TenantRoleService(f.Administration(), f.Roles, new NoOpTenantExtensionCoordinator());
        await Assert.ThrowsAsync<IdentityValidationException>(() => nativeService.RevokeRolesAsync(f.Tenant, f.AdminB, [f.Owner.RoleId]));
        Assert.IsTrue((await f.Members.FindStateAsync(f.Tenant, f.AdminB))!.IsEnabled);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task NativeGrantParticipatesInPartitionRevisionAndStaleSnapshotCannotOverwriteIt()
    {
        await using var f = await MembershipStorageFixture.Create();
        await f.Members.ConfigureProtectionAsync(f.Tenant, [f.Owner.RoleId]);
        var snapshot = await f.Members.ReadSnapshotAsync(f.Tenant, 5000); var original = snapshot.Members.Single(x => x.UserId == f.AdminA);
        await f.Roles.GrantRolesAsync(f.Tenant, f.Member, [f.Owner.RoleId]);
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => f.Members.ReplaceStateAsync(original with { IsEnabled = false }, original.Version, snapshot.Version, Guid.NewGuid()));
        Assert.IsTrue((await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.IsEnabled);
        var newest = await f.Members.ReadSnapshotAsync(f.Tenant, 5000); Assert.AreNotEqual(snapshot.Version, newest.Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task LostProjectionAcknowledgementReplaysOnePrimaryOperationAndRepairsOnlyCurrentState()
    {
        await using var f = await MembershipStorageFixture.Create();
        var wrapper = new SnapshotBarrier(f.Members) { WaitForPair = false, FailProjectionOnce = true };
        var service = f.Administration(wrapper); var original = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!; var op = Guid.NewGuid();
        await Assert.ThrowsAsync<IdentityProviderException>(() => service.SetEnabledAsync(f.Tenant, f.AdminA, false, original.Version, op, [f.Owner.RoleId]));
        var applied = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!; Assert.IsFalse(applied.IsEnabled); Assert.AreEqual(op, applied.LastOperationId);
        var recovered = await service.SetEnabledAsync(f.Tenant, f.AdminA, false, original.Version, op, [f.Owner.RoleId]);
        Assert.AreEqual(applied.Version, recovered.Version); Assert.IsFalse((await f.Members.GetTenantForUserAsync(f.AdminA, f.Tenant))!.IsActive);
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => service.SetEnabledAsync(f.Tenant, f.AdminA, true, original.Version, Guid.NewGuid(), [f.Owner.RoleId]));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task MalformedMembershipScopeCannotCountAsAnEligibleAdministrator()
    {
        await using var f = await MembershipStorageFixture.Create(); var original = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        await f.Primary.AddEntityAsync(new TableEntity("TEN|" + f.Tenant, "USR|" + Guid.NewGuid()) { ["TenantId"] = f.Tenant.ToString("D"),
            ["UserId"] = Guid.NewGuid().ToString("D"), ["Status"] = "Active", ["RoleIdsCsv"] = f.Owner.RoleId.ToString("D"), ["RolesCsv"] = "Owner" });
        await Assert.ThrowsAsync<IdentityProviderException>(() => f.Administration().SetEnabledAsync(f.Tenant, f.AdminA, false, original.Version, Guid.NewGuid(), [f.Owner.RoleId]));
        Assert.AreEqual(original.Version, (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task LegacyNameOnlyAdministratorCountsAccordingToExistingNativeRoleSemantics()
    {
        await using var f = await MembershipStorageFixture.Create();
        var row = (await f.Primary.GetEntityAsync<TableEntity>("TEN|" + f.Tenant, "USR|" + f.AdminA)).Value;
        row["RoleIdsCsv"] = ""; row["RolesCsv"] = "owner"; row["Status"] = "active"; await f.Primary.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace);
        Assert.AreEqual("Owner", (await f.Roles.GetRolesForUserAsync(f.Tenant, f.AdminA)).Single().Name);
        var b = (await f.Members.FindStateAsync(f.Tenant, f.AdminB))!;
        await f.Administration().SetEnabledAsync(f.Tenant, f.AdminB, false, b.Version, Guid.NewGuid(), [f.Owner.RoleId]);
        var legacy = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        await Assert.ThrowsAsync<IdentityValidationException>(() => f.Administration().SetEnabledAsync(f.Tenant, f.AdminA, false, legacy.Version, Guid.NewGuid(), [f.Owner.RoleId]));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)]
    public async Task AlreadyIssuedCustomerJwtAndRefreshLoseAccessImmediatelyAfterMembershipOrTenantChange(bool changeRoles, bool changeTenant)
    {
        await using var f = await MembershipStorageFixture.Create();
        var access = new IdentityTenantAccessValidator(f.Members, f.Tenants, new ServiceOperationExecutor());
        var jwtOptions = new JwtOptions { Issuer = "membership-fixture", Audience = "membership-fixture-api", SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) };
        var sessions = new Dictionary<string, AuthSessionRecord>(); var sessionStore = new Mock<IAuthSessionStore>();
        sessionStore.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>())).Callback((AuthSessionRecord row, CancellationToken _) => sessions[row.RefreshTokenHash] = row).Returns(Task.CompletedTask);
        sessionStore.Setup(x => x.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string hash, CancellationToken _) => sessions.GetValueOrDefault(hash));
        sessionStore.Setup(x => x.DeleteByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Callback((string hash, CancellationToken _) => sessions.Remove(hash)).Returns(Task.CompletedTask);
        var tokens = new JwtTokenService(Options.Create(jwtOptions), sessionStore.Object, Mock.Of<IAuthEventPublisher>(), Mock.Of<IAuthLifecycleHook>(),
            Options.Create(new AuthEventOptions()), NullLogger<JwtTokenService>.Instance, [new IdentityTenantAccessClaimsEnricher(access)]);
        var issued = await tokens.CreateAccessTokenAsync(f.AdminA, f.Tenant, [new("role", "Owner"), new("rid", f.Owner.RoleId.ToString("D"))]);
        var otherState = (await f.Members.FindStateAsync(f.OtherTenant, f.AdminA))!;
        var otherIssued = await tokens.CreateAccessTokenAsync(f.AdminA, f.OtherTenant, [new("role", "Owner"), new("rid", otherState.RoleIds.Single().ToString("D"))]);
        var b = WebApplication.CreateBuilder(); b.Logging.ClearProviders(); b.WebHost.UseTestServer();
        b.Services.AddSingleton<IIdentityTenantMembershipConditionalStore>(f.Members); b.Services.AddSingleton<ITenantMembershipStore>(f.Members);
        b.Services.AddSingleton<IIdentityTenantStore>(f.Tenants); b.Services.AddSingleton<ITenantRoleStore>(f.Roles); b.Services.AddSingleton<IServiceOperationExecutor>(new ServiceOperationExecutor());
        b.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(x => x.TokenValidationParameters = new() { ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer, ValidateAudience = true, ValidAudience = jwtOptions.Audience, ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)), ValidateLifetime = true });
        b.Services.AddAuthorization(); b.Services.AddIBeamIdentityTenantAccessEnforcementApi();
        await using var app = b.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapGet("/private", () => "authorized").RequireAuthorization();
        app.MapGet("/admin", () => "administrator").RequireAuthorization(policy => policy.RequireRole("Owner"));
        await app.StartAsync(); using var client = app.GetTestClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", issued.AccessToken);
        using var before = await client.GetAsync("/private"); Assert.AreEqual(HttpStatusCode.OK, before.StatusCode);
        var original = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        IdentityTenantMembershipState disabled;
        if (changeTenant) {
            var tenant = (await f.Tenants.FindVersionedAsync(f.Tenant))!;
            await f.Tenants.SetStatusConditionalAsync(f.Tenant, false, tenant.Version!, Guid.NewGuid()); disabled = original;
            await Assert.ThrowsAsync<IdentityValidationException>(() => f.Roles.EnsureTenantMembershipAndGrantRolesAsync(new(f.Tenant, f.AdminA, RoleIds: [f.Owner.RoleId])));
            Assert.AreEqual(original.Version, (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.Version);
        } else disabled = changeRoles ? await f.Administration().SetRolesAsync(f.Tenant, f.AdminA, [], original.Version, Guid.NewGuid(), [f.Owner.RoleId]) :
            await f.Administration().SetEnabledAsync(f.Tenant, f.AdminA, false, original.Version, Guid.NewGuid(), [f.Owner.RoleId]);
        using var denied = await client.GetAsync("/private"); Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        await Assert.ThrowsAsync<IdentityUnauthorizedException>(() => tokens.RefreshAccessTokenAsync(issued.RefreshToken!));
        client.DefaultRequestHeaders.Authorization = new("Bearer", otherIssued.AccessToken);
        using var otherAllowed = await client.GetAsync("/private"); Assert.AreEqual(HttpStatusCode.OK, otherAllowed.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", issued.AccessToken);
        if (changeTenant) {
            var tenant = (await f.Tenants.FindVersionedAsync(f.Tenant))!; await f.Tenants.SetStatusConditionalAsync(f.Tenant, true, tenant.Version!, Guid.NewGuid());
        } else await f.Administration().SetEnabledAsync(f.Tenant, f.AdminA, true, disabled.Version, Guid.NewGuid(), [f.Owner.RoleId]);
        using var stillOld = await client.GetAsync("/private"); Assert.AreEqual(HttpStatusCode.Unauthorized, stillOld.StatusCode);
        await Assert.ThrowsAsync<IdentityUnauthorizedException>(() => tokens.RefreshAccessTokenAsync(issued.RefreshToken!));
        if (changeRoles) {
            var fresh = await tokens.CreateAccessTokenAsync(f.AdminA, f.Tenant, []); client.DefaultRequestHeaders.Authorization = new("Bearer", fresh.AccessToken);
            using var member = await client.GetAsync("/private"); Assert.AreEqual(HttpStatusCode.OK, member.StatusCode);
            using var notAdmin = await client.GetAsync("/admin"); Assert.AreEqual(HttpStatusCode.Forbidden, notAdmin.StatusCode);
        }
    }
}

internal sealed class MembershipStorageFixture : IAsyncDisposable
{
    public Guid Tenant { get; } = Guid.NewGuid(); public Guid OtherTenant { get; } = Guid.NewGuid();
    public Guid AdminA { get; } = Guid.NewGuid(); public Guid AdminB { get; } = Guid.NewGuid(); public Guid Member { get; } = Guid.NewGuid();
    public TableClient Primary { get; private set; } = null!; public TableClient Secondary { get; private set; } = null!;
    public TableClient RoleTable { get; private set; } = null!; public TableClient TenantTable { get; private set; } = null!;
    public AzureTableTenantMembershipStore Members { get; private set; } = null!; public AzureTableTenantRoleStore Roles { get; private set; } = null!;
    public AzureTableIdentityTenantStore Tenants { get; private set; } = null!;
    public TenantRole Owner { get; private set; } = null!; public TenantRole MemberRole { get; private set; } = null!;
    public IdentityTenantMembershipAdministrationService Administration(IIdentityTenantMembershipConditionalStore? store = null) => new(store ?? Members, Roles, Tenants, new ServiceOperationExecutor());
    public static async Task<MembershipStorageFixture> Create(string? connectionOverride = null, bool ownershipProtection = false, string ownerRoleName = "Owner")
    {
        var connection = connectionOverride ?? Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE"); if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var f = new MembershipStorageFixture(); var suffix = Guid.NewGuid().ToString("N");
        f.Primary = new(connection, "MemberPrimary" + suffix); f.Secondary = new(connection, "MemberSecondary" + suffix);
        f.RoleTable = new(connection, "MemberRoles" + suffix); f.TenantTable = new(connection, "MemberTenants" + suffix);
        try {
            foreach (var table in new[] { f.Primary, f.Secondary, f.RoleTable, f.TenantTable }) await table.CreateIfNotExistsAsync();
            var options = Options.Create(new AzureTableIdentityOptions { TenantUsersTableName = f.Primary.Name, UserTenantsTableName = f.Secondary.Name,
                TenantRolesTableName = f.RoleTable.Name, TenantsTableName = f.TenantTable.Name, CreateTablesIfNotExists = false,
                RequireOwnershipProtection = ownershipProtection }); var tables = new TableServiceClient(connection);
            f.Members = new(tables, options); f.Roles = new(tables, options); f.Tenants = new(tables, options);
            await f.Tenants.CreateAsync(new(f.Tenant, "Private enterprise")); await f.Tenants.CreateAsync(new(f.OtherTenant, "Other enterprise"));
            f.Owner = await f.Roles.CreateRoleAsync(f.Tenant, ownerRoleName, true); f.MemberRole = await f.Roles.CreateRoleAsync(f.Tenant, "Member");
            foreach (var user in new[] { f.AdminA, f.AdminB, f.Member }) await f.Seed(f.Tenant, user, user == f.Member ? f.MemberRole : f.Owner, user == f.AdminA);
            var otherRole = await f.Roles.CreateRoleAsync(f.OtherTenant, "Owner", true); await f.Seed(f.OtherTenant, f.AdminA, otherRole, false);
            return f;
        } catch { await f.DisposeAsync(); throw; }
    }
    private async Task Seed(Guid tenant, Guid user, TenantRole role, bool isDefault)
    {
        await Primary.AddEntityAsync(new TableEntity("TEN|" + tenant, "USR|" + user) { ["TenantId"] = tenant.ToString("D"), ["UserId"] = user.ToString("D"),
            ["Status"] = "Active", ["RoleIdsCsv"] = role.RoleId.ToString("D"), ["RolesCsv"] = role.Name, ["CreatedAt"] = DateTimeOffset.UtcNow,
            ["ConsumerColumn"] = "KEEP-CONSUMER-COLUMN", ["Email"] = "PRIVATE-CUSTOMER-EMAIL", ["PhoneNumber"] = "PRIVATE-CUSTOMER-PHONE" });
        await Secondary.AddEntityAsync(new TableEntity("USR|" + user, "TEN|" + tenant) { ["TenantId"] = tenant.ToString("D"), ["UserId"] = user.ToString("D"),
            ["Status"] = "Active", ["RoleIdsCsv"] = role.RoleId.ToString("D"), ["RolesCsv"] = role.Name, ["IsDefault"] = isDefault, ["CreatedAt"] = DateTimeOffset.UtcNow });
    }
    public async ValueTask DisposeAsync() { foreach (var table in new[] { Primary, Secondary, RoleTable, TenantTable }) if (table is not null) await table.DeleteAsync(); }
}

internal sealed class SnapshotBarrier(IIdentityTenantMembershipConditionalStore inner) : IIdentityTenantOwnershipConditionalStore
{
    public int OwnershipProtocolVersion => (inner as IIdentityTenantOwnershipWriter)?.OwnershipProtocolVersion ?? 0;
    public bool RequiresOwnershipProtection => (inner as IIdentityTenantOwnershipWriter)?.RequiresOwnershipProtection ?? false;
    public Task ConfigureOwnershipProtectionAsync(Guid tenant, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    { if (FailOwnershipOnce) { FailOwnershipOnce = false; throw new IdentityProviderException("PRIVATE-OWNERSHIP-PROVIDER-FAILURE"); }
      return inner is IIdentityTenantOwnershipConditionalStore owned ? owned.ConfigureOwnershipProtectionAsync(tenant, ids, ct)
        : throw new IdentityProviderException("Ownership protocol is unavailable."); }
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously); private int reads;
    public bool WaitForPair { get; set; } = true; public bool FailProjectionOnce { get; set; }
    public bool FailOwnershipOnce { get; set; }
    public bool DropOwnershipMetadata { get; set; }
    public Func<Guid, Guid, CancellationToken, Task>? BeforeFindState { get; set; }
    public Task ConfigureProtectionAsync(Guid tenant, IReadOnlyList<Guid> ids, CancellationToken ct = default) => inner.ConfigureProtectionAsync(tenant, ids, ct);
    public async Task<IdentityTenantMembershipSnapshot> ReadSnapshotAsync(Guid tenant, int limit, CancellationToken ct = default)
    { var result = await inner.ReadSnapshotAsync(tenant, limit, ct); if (WaitForPair) { if (Interlocked.Increment(ref reads) == 2) ready.TrySetResult(); await ready.Task.WaitAsync(ct); }
      return DropOwnershipMetadata ? result with { ProtectedOwnerRoleIds = [], OwnershipProtocolVersion = 0 } : result; }
    public async Task<IdentityTenantMembershipState?> FindStateAsync(Guid tenant, Guid user, CancellationToken ct = default)
    { if (BeforeFindState is not null) await BeforeFindState(tenant, user, ct); return await inner.FindStateAsync(tenant, user, ct); }
    public Task<IdentityTenantMembershipState> ReplaceStateAsync(IdentityTenantMembershipState next, string memberVersion, string tenantVersion, Guid operation, CancellationToken ct = default)
        => inner.ReplaceStateAsync(next, memberVersion, tenantVersion, operation, ct);
    public Task RepairProjectionAsync(Guid tenant, Guid user, CancellationToken ct = default)
    { if (FailProjectionOnce) { FailProjectionOnce = false; throw new IdentityProviderException("PRIVATE-PROJECTION-FAILURE"); } return inner.RepairProjectionAsync(tenant, user, ct); }
}
