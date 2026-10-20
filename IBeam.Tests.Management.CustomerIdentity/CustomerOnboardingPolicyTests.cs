using System.Text.Json;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Onboarding;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerOnboardingPolicyTests
{
    [TestMethod]
    public async Task CatalogOnlyReturnsEnabledKeysWithoutImplementationOrRoleDetails()
    {
        var f = new OnboardingFixture(); f.Policy.Policies["medinsights"] = "Administrator";
        f.Policy.Policies["disabled"] = "Owner"; f.Policy.DisabledPolicyKeys.Add("disabled");
        f.Policy.Initializers["medinsights"] = "clinical-setup";
        f.Operation.Actor = f.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingRead };
        var rows = await new ManagementCustomerOnboardingPolicyCatalog(new ConfiguredManagementCustomerOnboardingPolicy(Options.Create(f.Policy)), f, new ServiceOperationExecutor()).ListAsync();
        Assert.HasCount(2, rows); Assert.IsTrue(rows.Single(x => x.Key == "medinsights").RequiresInitialization);
        var json = JsonSerializer.Serialize(rows); Assert.DoesNotContain("disabled", json); Assert.DoesNotContain("clinical-setup", json); Assert.DoesNotContain("Administrator", json);
    }
    [TestMethod]
    public async Task DisabledPoliciesDenyBeforeAnyCustomerWriteAndCannotResumePersistedIntent()
    {
        var hook = new Initializer { FailBefore = true }; var f = WithHook(hook);
        var first = await f.Service.CreateAsync(f.Request, "policy-disabled-01"); Assert.AreEqual("recoverable-failure", first.Status);
        f.Policy.DisabledPolicyKeys.Add(f.Request.PolicyKey);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.CreateAsync(f.Request, "policy-disabled-01"));
        Assert.AreEqual(1, f.Tenants.Store.Creates); Assert.AreEqual(0, f.Sends); Assert.HasCount(0, f.InviteRows);
        var fresh = new OnboardingFixture(); fresh.Policy.DisabledPolicyKeys.Add(fresh.Request.PolicyKey);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => fresh.Service.CreateAsync(fresh.Request, "policy-disabled-02"));
        Assert.AreEqual(0, fresh.Tenants.Store.Creates);
    }
    [TestMethod]
    public async Task RequiredInitializationFailureIsSafeAndRecoveryKeepsStableIntent()
    {
        var hook = new Initializer { FailBefore = true }; var f = WithHook(hook);
        var first = await f.Service.CreateAsync(f.Request, "policy-recovery-01");
        Assert.AreEqual("consumer-initialization", first.Step); Assert.AreEqual("consumer-initialization-incomplete", first.ErrorCode);
        Assert.AreEqual(0, f.Sends); Assert.HasCount(0, f.InviteRows); Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(first));
        hook.FailBefore = false;
        var resumed = await f.Service.CreateAsync(f.Request, "policy-recovery-01");
        Assert.AreEqual(first.TenantId, resumed.TenantId); Assert.AreEqual(first.OperationId, hook.Context!.OperationId);
        Assert.AreEqual(first.OnboardingId, hook.Context.OnboardingId); Assert.AreEqual("awaiting-acceptance", resumed.Status);
        Assert.AreEqual(1, hook.Resources); Assert.AreEqual(1, f.Sends);
    }
    [TestMethod]
    public async Task LostHookAcknowledgementReconcilesWithoutDuplicatingConsumerResource()
    {
        var hook = new Initializer { FailAfter = true }; var f = WithHook(hook);
        Assert.AreEqual("recoverable-failure", (await f.Service.CreateAsync(f.Request, "policy-lost-ack-01")).Status);
        hook.FailAfter = false;
        Assert.AreEqual("awaiting-acceptance", (await f.Service.CreateAsync(f.Request, "policy-lost-ack-01")).Status);
        Assert.AreEqual(1, hook.Calls); Assert.AreEqual(1, hook.Resources); Assert.AreEqual(1, f.Tenants.Store.Creates);
    }
    [TestMethod]
    public async Task UnknownHookStateNeverBlindlyInitializesOrSends()
    {
        var hook = new Initializer { Unknown = true }; var f = WithHook(hook);
        Assert.AreEqual("recoverable-failure", (await f.Service.CreateAsync(f.Request, "policy-unknown-01")).Status);
        Assert.AreEqual(0, hook.Calls); Assert.AreEqual(0, f.Sends);
    }
    [TestMethod]
    public async Task MissingRequiredHookBlocksInvitationAndChangedPolicyCannotBroadenSavedIntent()
    {
        var f = new OnboardingFixture(); f.Policy.Initializers[f.Request.PolicyKey] = "missing";
        Assert.AreEqual("consumer-initialization-incomplete", (await f.Service.CreateAsync(f.Request, "policy-missing-01")).ErrorCode);
        Assert.HasCount(0, f.InviteRows); f.Policy.Initializers.Clear();
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.CreateAsync(f.Request, "policy-missing-01"));
    }
    [TestMethod]
    public async Task TwoConsumerPoliciesKeepIndependentInitializationAndIdentityOnlyNeedsNone()
    {
        var firstHook = new Initializer(); var secondHook = new Initializer();
        var first = WithHook(firstHook); var second = WithHook(secondHook); second.Policy.Policies["enterprise-member"] = "Administrator"; second.LocalRoleName = "Administrator"; second.Policy.Initializers["enterprise-member"] = secondHook.Key;
        await first.Service.CreateAsync(first.Request, "consumer-policy-01"); await second.Service.CreateAsync(new() { TenantName = second.Request.TenantName, InitialAdminEmail = second.Request.InitialAdminEmail, PolicyKey = "enterprise-member" }, "consumer-policy-01");
        Assert.AreNotEqual(firstHook.Context!.IdentityRealmId, secondHook.Context!.IdentityRealmId);
        Assert.AreEqual(1, firstHook.Resources); Assert.AreEqual(1, secondHook.Resources);
        var identityOnly = new OnboardingFixture(); Assert.AreEqual("awaiting-acceptance", (await identityOnly.Service.CreateAsync(identityOnly.Request, "identity-only-01")).Status);
    }
    [TestMethod]
    public async Task NativeExtensionBridgeReconcilesExistingRowAndNeverAssignsCustomerActor()
    {
        var tenant = Guid.NewGuid(); var operation = Guid.NewGuid(); var tenants = new Mock<IIdentityTenantStore>(); var store = new Mock<ITenantExtensionStore<Extension>>();
        tenants.Setup(x => x.FindByIdAsync(tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new IdentityTenant(tenant, "Customer"));
        Extension? row = null; TenantExtensionContext? delivered = null; var creates = 0;
        store.Setup(x => x.FindByTenantIdAsync(tenant, It.IsAny<CancellationToken>())).ReturnsAsync(() => row);
        store.Setup(x => x.CreateAsync(It.IsAny<IdentityTenant>(), It.IsAny<TenantExtensionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityTenant t, TenantExtensionContext c, CancellationToken _) => { creates++; delivered = c; return row = new(t.TenantId); });
        var hook = new TenantExtensionOnboardingInitializer<Extension>("extension", tenants.Object, store.Object);
        var context = new ManagementCustomerInitializationContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), operation, Guid.NewGuid(), tenant, "enterprise");
        await hook.InitializeAsync(context); await hook.InitializeAsync(context);
        Assert.AreEqual(1, creates); Assert.IsNull(delivered!.AuthUserId); Assert.AreEqual(operation.ToString("D"), delivered.CausationId);
        Assert.IsTrue(delivered.RequireLifecycleDelivery); Assert.AreEqual(ManagementCustomerInitializationState.Applied, await hook.ReconcileAsync(context));
    }
    [TestMethod]
    public async Task PolicyCatalogRequiresLiveAuthority()
    {
        var f = new OnboardingFixture { Allowed = false };
        var catalog = new ManagementCustomerOnboardingPolicyCatalog(new ConfiguredManagementCustomerOnboardingPolicy(Options.Create(f.Policy)), f, new ServiceOperationExecutor());
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => catalog.ListAsync());
    }
    private static OnboardingFixture WithHook(Initializer hook) { var f = new OnboardingFixture(hook); f.Policy.Initializers[f.Request.PolicyKey] = hook.Key; return f; }
    public sealed record Extension(Guid TenantId) : IIdentityTenantExtension;
    private sealed class Initializer : IManagementCustomerOnboardingInitializer
    {
        public string Key => "consumer-setup";
        public bool FailBefore, FailAfter, Unknown;
        public int Calls, Resources;
        public ManagementCustomerInitializationContext? Context;
        public Task<ManagementCustomerInitializationState> ReconcileAsync(ManagementCustomerInitializationContext context, CancellationToken ct = default)
            => Task.FromResult(Unknown ? ManagementCustomerInitializationState.Unknown : Resources == 0 ? ManagementCustomerInitializationState.Absent : ManagementCustomerInitializationState.Applied);
        public Task InitializeAsync(ManagementCustomerInitializationContext context, CancellationToken ct = default)
        { Calls++; Context = context; if (FailBefore) throw new Exception("PRIVATE provider details"); Resources = 1; if (FailAfter) throw new Exception("PRIVATE lost acknowledgement"); return Task.CompletedTask; }
    }
}

