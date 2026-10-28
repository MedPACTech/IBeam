using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Identity.Services.Tenants;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Operations;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.Services;
using IBeam.Management.TenantData;
using IBeam.Management.TenantData.Identity;
using IBeam.Management.TenantData.Target.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.TenantData;

internal sealed class IdentityPurgeFixture : IAsyncDisposable
{
    public Guid Tenant { get; } = Guid.NewGuid(); public Guid OtherTenant { get; } = Guid.NewGuid();
    public Guid User { get; } = Guid.NewGuid(); public Guid OtherAdmin { get; } = Guid.NewGuid();
    public Guid Credential { get; } = Guid.NewGuid(); public Guid Agent { get; } = Guid.NewGuid();
    public Guid Invite { get; } = Guid.NewGuid(); public Guid Actor { get; } = Guid.NewGuid();
    public ServiceProvider Provider { get; private set; } = null!;
    public GateService Storage { get; private set; } = null!;
    public AzureTableIdentityOptions NativeOptions { get; private set; } = null!;
    public ManagementTenantPurgeExecution Execution { get; private set; } = null!;
    public ManagementTenantPurgePlan Plan { get; private set; } = null!;
    public AppHandler App { get; } = new();
    public ConcurrentBag<ManagementAuditEntity> Audits { get; } = [];
    private readonly List<TableClient> owned = [];
    private string prefix = "";
    private IdentityPurgeFixture() { }
    public static async Task<IdentityPurgeFixture> Create(int extraMembers = 0, bool includeAppFence = true)
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE") ?? "";
        var endpoint = Regex.Match(connection, "(?:^|;)TableEndpoint=([^;]+)");
        if (!endpoint.Success || !Uri.TryCreate(endpoint.Groups[1].Value, UriKind.Absolute, out var uri) || !uri.IsLoopback ||
            !connection.Contains("AccountName=devstoreaccount1;", StringComparison.Ordinal)) Assert.Inconclusive("An owned loopback Table fixture is required.");
        var f = new IdentityPurgeFixture(); f.prefix = "P" + Guid.NewGuid().ToString("N")[..12]; f.Storage = new(connection);
        f.NativeOptions = new() { TablePrefix = f.prefix, StorageConnectionString = connection, CreateTablesIfNotExists = false, RequireOwnershipProtection = true, RequireTenantPurgeFencing = true };
        foreach (var suffix in new[] { "Tenants", "TenantUsers", "UserTenants", "Roles", "TenantInvites", "ApiCredentials", "AgentUsers", "AgentUserCredentials", "OAuthConsents", "AccessCatalogOverrides", "PurgeJournal", "AspNetUsers", "OtpChallenges", "AuthSessions" }) {
            var table = f.Storage.GetTableClient(f.prefix + suffix); await table.CreateIfNotExistsAsync(); f.owned.Add(table);
        }
        var services = new ServiceCollection(); services.AddSingleton<TableServiceClient>(f.Storage); services.AddSingleton(Options.Create(f.NativeOptions));
        services.AddSingleton<IHostEnvironment>(new Host()); services.AddSingleton(f.App);
        services.AddScoped<AzureTableIdentityTenantStore>(); services.AddScoped<IIdentityTenantStore>(p => p.GetRequiredService<AzureTableIdentityTenantStore>());
        services.AddScoped<AzureTableTenantMembershipStore>(); services.AddScoped<IIdentityTenantMembershipConditionalStore>(p => p.GetRequiredService<AzureTableTenantMembershipStore>());
        services.AddScoped<ITenantMembershipStore>(p => p.GetRequiredService<AzureTableTenantMembershipStore>());
        services.AddScoped<AzureTableTenantRoleStore>(); services.AddScoped<ITenantRoleStore>(p => p.GetRequiredService<AzureTableTenantRoleStore>());
        services.AddScoped<AzureTableTenantInviteStore>(); services.AddIBeamIdentityAzureTableTenantPurge();
        if (includeAppFence) services.AddSingleton<IManagementTenantPurgeAccessFence>(f.App);
        services.AddIBeamManagementTenantPurgeIdentity<AppHandler>(o => o.Enabled = true);
        services.AddIBeamManagementAzureTableTenantPurgeJournal(f.Table("PurgeJournal"));
        services.AddIBeamManagementTenantPurgeJobs(new ConfigurationBuilder().Build());
        services.AddIBeamManagementTenantPurgeTarget(o => { o.Enabled = true; o.PolicyKeys = ["enterprise"]; });
        services.AddSingleton<IServiceOperationExecutor>(new ServiceOperationExecutor());
        services.AddScoped<IManagementTenantPurgePolicy, Policy>();
        var access = new Mock<IManagementOperationAccessService>(); access.SetupGet(x => x.ProtocolVersion).Returns(1);
        access.Setup(x => x.RequireAsync(ManagementTenantDataOperations.Delete, It.IsAny<ManagementOperationContext>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask); services.AddSingleton(access.Object);
        var audit = new Mock<IManagementAuditRepository>(); audit.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>())).Callback<ManagementAuditEntity, CancellationToken>((e, _) => f.Audits.Add(e)).Returns(Task.CompletedTask); services.AddSingleton(audit.Object);
        var realm = Guid.NewGuid(); var org = Guid.NewGuid(); var app = Guid.NewGuid();
        services.Configure<ManagementDiagnosticsOptions>(o => { o.ApplicationKey = "fixture"; o.Environment = "Testing"; });
        services.Configure<ManagementOperationTargetOptions>(o => { o.RealmId = realm; o.DedicatedApplicationRealm = true; o.EnabledOperations = [ManagementTenantDataOperations.Delete]; o.TrustedPortals = [new() { OrganizationId = org, ApplicationId = app }]; });
        f.Provider = services.BuildServiceProvider();
        using (var scope = f.Provider.CreateScope()) {
            var p = scope.ServiceProvider; var tenants = p.GetRequiredService<AzureTableIdentityTenantStore>(); var roles = p.GetRequiredService<AzureTableTenantRoleStore>(); var members = p.GetRequiredService<AzureTableTenantMembershipStore>();
            foreach (var tenant in new[] { f.Tenant, f.OtherTenant }) {
                await tenants.CreateAsync(new(tenant, "PRIVATE-TENANT"));
                await roles.EnsureTenantMembershipAndGrantRolesAsync(new(tenant, f.User, "PRIVATE-TENANT", RoleNames: ["Owner", "Administrator"], SetAsDefault: false));
                await roles.EnsureTenantMembershipAndGrantRolesAsync(new(tenant, f.OtherAdmin, "PRIVATE-TENANT", RoleNames: ["Administrator"], SetAsDefault: false));
                var definitions = await roles.GetRolesAsync(tenant);
                await members.ConfigureOwnershipProtectionAsync(tenant, [definitions.Single(x => x.Name == "Owner").RoleId]);
                await members.ConfigureProtectionAsync(tenant, [definitions.Single(x => x.Name == "Administrator").RoleId]);
            }
            for (var i = 0; i < extraMembers; i++) await roles.EnsureTenantMembershipAndGrantRolesAsync(new(f.Tenant, Guid.NewGuid(), "PRIVATE-TENANT", RoleNames: ["Administrator"], SetAsDefault: false));
            await members.SetDefaultTenantAsync(f.User, f.OtherTenant);
            var invites = p.GetRequiredService<AzureTableTenantInviteStore>();
            await invites.CreateAsync(f.Invitation());
            // Historical orphan index: native metadata proves exact tenant ownership; no primary exists.
            await f.Table("TenantInvites").AddEntityAsync(new TableEntity("TOK|stale-token-hash", "INV") { ["TenantId"] = f.Tenant.ToString("D"), ["InviteId"] = Guid.NewGuid().ToString("D"), ["NormalizedDestination"] = "PRIVATE-OLD-RECIPIENT" });
            await f.SeedAuxiliary(f.Tenant); await f.SeedAuxiliary(f.OtherTenant);
            foreach (var suffix in new[] { "AspNetUsers", "OtpChallenges", "AuthSessions" }) await f.Table(suffix).AddEntityAsync(new TableEntity("GLOBAL", f.User.ToString("D")) { ["Value"] = "PRIVATE-GLOBAL-" + suffix });
            var version = (await tenants.FindVersionedAsync(f.Tenant))!.Version; var job = Guid.NewGuid();
            f.Execution = new(new(org, app, realm, f.Tenant, f.Actor, job) { ResourceId = f.Tenant, ResourceVersion = version }, "fixture", "Testing", job, "enterprise",
                ManagementTenantPurgeProtocol.Fingerprint(p.GetRequiredService<IManagementTenantPurgeHandler>().Describe()));
        }
        return f;
    }
    public TableClient Table(string name) => Storage.GetTableClient(prefix + name);
    public TenantInviteRecord Invitation() => new(Invite, Tenant, "email", "PRIVATE-RECIPIENT@example.test", "synthetic-token-hash", "pending", DateTimeOffset.UtcNow, User, DateTimeOffset.UtcNow.AddHours(1));
    private async Task SeedAuxiliary(Guid tenant)
    {
        var o = NativeOptions; var pk = "TEN|" + tenant.ToString("D");
        await Table("ApiCredentials").AddEntityAsync(new TableEntity(pk, o.ApiCredentialsRk(Credential)) { ["TenantId"] = tenant.ToString("D"), ["CredentialId"] = Credential.ToString("D"), ["SecretHash"] = "PRIVATE-KEY-HASH", ["IsDeleted"] = false });
        await Table("AgentUsers").AddEntityAsync(new TableEntity(pk, o.AgentUsersRk(Agent)) { ["TenantId"] = tenant.ToString("D"), ["AgentUserId"] = Agent.ToString("D"), ["MetadataJson"] = "PRIVATE-AGENT" });
        await Table("AgentUserCredentials").AddEntityAsync(new TableEntity(o.AgentUserCredentialsPk(tenant, Agent), o.AgentUserCredentialsRk(Credential)) { ["TenantId"] = tenant.ToString("D"), ["AgentUserId"] = Agent.ToString("D"), ["CredentialId"] = Credential.ToString("D") });
        await Table("AgentUserCredentials").AddEntityAsync(new TableEntity(o.AgentCredentialIndexPk(tenant), o.AgentCredentialIndexRk(Credential)) { ["TenantId"] = tenant.ToString("D"), ["AgentUserId"] = Agent.ToString("D") });
        await Table("OAuthConsents").AddEntityAsync(new TableEntity(o.OAuthConsentsPk(tenant, User), o.OAuthConsentsRk("fixture", "resource")) { ["TenantId"] = tenant.ToString("D"), ["UserId"] = User.ToString("D"), ["ScopesCsv"] = "PRIVATE-SCOPES" });
        await Table("AccessCatalogOverrides").AddEntityAsync(new TableEntity(pk, o.AccessCatalogOverridesRk(Guid.NewGuid())) { ["TenantId"] = tenant.ToString("D"), ["Description"] = "PRIVATE-CATALOG" });
    }
    public async Task Confirm()
    {
        Storage.Failures.Clear();
        using var scope = Provider.CreateScope(); var p = scope.ServiceProvider;
        var target = p.GetRequiredService<IManagementOperationHandler<ManagementTenantPurgeRequest, ManagementTenantPurgeResult>>();
        var planId = Guid.NewGuid(); var planScope = Execution.Authority with { OperationId = planId };
        Plan = (await target.ExecuteAsync(planScope, new("plan", "enterprise", Execution.Authority.ResourceVersion!, null, null))).Plan!;
        await target.ExecuteAsync(Execution.Authority, new("confirm", "enterprise", Plan.ExpectedVersion, Plan.PlanId, Plan.ConfirmationBinding));
    }
    public async Task<ManagementTenantPurgeResult> Status()
    { using var scope = Provider.CreateScope(); return await scope.ServiceProvider.GetRequiredService<IManagementTenantPurgeJobService>().FindAsync(Execution); }
    public async Task Run(bool reconcile = false)
    { using var scope = Provider.CreateScope(); await scope.ServiceProvider.GetRequiredService<IManagementTenantPurgeWorker>().RunAsync(Execution, reconcile); }
    public async Task Complete()
    {
        for (var i = 0; i < 160; i++) {
            await Run(); var state = (await Status()).Job!.State;
            if (state == "succeeded") return;
            if (state is "failed" or "unknown") {
                var stages = new List<string>();
                foreach (var table in owned.Where(x => !x.Name.EndsWith("PurgeJournal", StringComparison.Ordinal))) {
                    var row = await table.GetEntityIfExistsAsync<TableEntity>("PURGE|" + Tenant.ToString("D"), "!IBEAM|PURGE|ROOT");
                    if (row.HasValue) stages.Add(table.Name[prefix.Length..] + ":" + (row.Value.GetString("Phase") ?? "active"));
                }
                Assert.Fail("Native recovery state " + state + "; checkpoints " + string.Join(",", stages) + "; safe storage codes " + string.Join(",", Storage.Failures.TakeLast(3)));
            }
        }
        Assert.Fail("Native cleanup exceeded its bounded fixture iterations.");
    }
    public IdentityTenantPurgeScope NativeScope() => new(Execution.ApplicationKey, Execution.Environment, Execution.Authority.OrganizationId, Execution.Authority.ApplicationId,
        Execution.Authority.RealmId, Tenant, Actor, Execution.JobId, Execution.Authority.ResourceVersion!);
    public async ValueTask DisposeAsync()
    {
        Storage.Release(); await Provider.DisposeAsync();
        foreach (var table in owned) { if (!table.Name.StartsWith(prefix, StringComparison.Ordinal) || !table.Uri.IsLoopback) throw new InvalidOperationException("Fixture table ownership differs."); await table.DeleteAsync(); }
    }
    public sealed class AppHandler : IManagementTenantPurgeHandler, IManagementTenantPurgeAccessFence
    {
        private readonly ConcurrentDictionary<Guid, Guid> fences = new(); public ConcurrentDictionary<Guid, byte> Effects { get; } = new();
        public int ProtocolVersion => 1; public string Key => "fixture-app";
        public ManagementTenantPurgeDefinition Describe() => new(1, "synthetic-app", 1, Array.AsReadOnly(new[] { new ManagementTenantPurgeCategory("app-data", "dataset", "delete"), new("global-accounts", "identity-metadata", "exclude") }));
        public Task PreviewAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) => Task.CompletedTask;
        public async Task PurgeOrReconcileAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) { await e.LeaseFence!.RequireAsync(ct); Effects.TryAdd(e.JobId, 0); }
        public Task<IReadOnlyList<ManagementTenantPurgeCategory>> VerifyAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ManagementTenantPurgeCategory>>([new("app-data", "dataset", "deleted"), new("global-accounts", "identity-metadata", "excluded")]);
        public async Task FenceOrReconcileAsync(ManagementTenantPurgeExecution e, string version, CancellationToken ct = default) { await e.LeaseFence!.RequireAsync(ct); if (fences.GetOrAdd(e.Authority.TenantId, e.JobId) != e.JobId) throw new ManagementOperationConflictException(); }
        public Task RequireFencedAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) { if (fences.GetValueOrDefault(e.Authority.TenantId) != e.JobId) throw new ManagementAccessDeniedException(); return Task.CompletedTask; }
    }
    private sealed class Policy(IIdentityTenantStore tenants, IIdentityTenantPurgeService identity) : IManagementTenantPurgePolicy
    {
        public int ProtocolVersion => 1;
        public async Task RequireAsync(ManagementTenantPurgeExecution e, string version, CancellationToken ct = default) {
            var current = await ((IIdentityTenantConditionalStore)tenants).FindVersionedAsync(e.Authority.TenantId, ct);
            if (current is null || !current.Tenant.IsActive || current.Version != version) throw new ManagementOperationPreconditionException();
        }
        public async Task RequireStatusAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) {
            if (await tenants.FindByIdAsync(e.Authority.TenantId, ct) is not null) return;
            if (!await identity.HasPurgeMarkerAsync(new(e.ApplicationKey, e.Environment, e.Authority.OrganizationId, e.Authority.ApplicationId,
                e.Authority.RealmId, e.Authority.TenantId, e.Authority.ActorUserId, e.JobId, e.Authority.ResourceVersion!), ct)) throw new ManagementAccessDeniedException();
        }
    }
    private sealed class Host : IHostEnvironment { public string EnvironmentName { get; set; } = "Testing"; public string ApplicationName { get; set; } = "fixture"; public string ContentRootPath { get; set; } = "."; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
    public sealed class GateService(string connection) : TableServiceClient(connection)
    {
        public Func<TableTransactionAction, bool>? Match;
        public ConcurrentQueue<string> Failures { get; } = new();
        public string? LoseDeleteTable;
        private int lostReply;
        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int held;
        public void Release() => release.TrySetResult();
        public override TableClient GetTableClient(string tableName) => new GateTable(connection, tableName, this);
        private sealed class GateTable(string connection, string name, GateService owner) : TableClient(connection, name)
        {
            public override async Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(IEnumerable<TableTransactionAction> transactionActions, CancellationToken cancellationToken = default) {
                var actions = transactionActions.ToArray();
                if (owner.Match is { } match && actions.Any(match) && Interlocked.CompareExchange(ref owner.held, 1, 0) == 0) { owner.Entered.TrySetResult(); await owner.release.Task.WaitAsync(cancellationToken); }
                try {
                    var result = await base.SubmitTransactionAsync(actions, cancellationToken);
                    if (owner.LoseDeleteTable == name && actions.Any(x => x.ActionType == TableTransactionActionType.Delete) && Interlocked.CompareExchange(ref owner.lostReply, 1, 0) == 0)
                        throw new IOException("PRIVATE-SYNTHETIC-LOST-DELETE-REPLY");
                    return result;
                }
                catch (RequestFailedException ex) { owner.Failures.Enqueue(ex.Status + ":" + ex.ErrorCode); throw; }
            }
        }
    }
}

