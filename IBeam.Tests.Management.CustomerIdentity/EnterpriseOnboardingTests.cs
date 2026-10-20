using System.Text.Json;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Exceptions;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Onboarding;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Management.Identity.Target.Services.Tenants;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class EnterpriseOnboardingTests
{
    [TestMethod]
    public async Task RepeatedRequestsReserveOneTenantAndInviteAndMailDoesNotCompleteOnboarding()
    {
        var f = new OnboardingFixture(); var first = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        var second = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        Assert.AreEqual(first, second); Assert.AreEqual("awaiting-acceptance", first.Status); Assert.IsFalse(first.Completed);
        Assert.AreEqual("sent", first.DeliveryStatus); Assert.AreEqual("invitation-persisted", first.Step);
        Assert.AreEqual(1, f.Tenants.Store.Creates); Assert.AreEqual(1, f.Sends); Assert.HasCount(1, f.InviteRows);
        Assert.AreEqual(Guid.Empty, f.InviteRows.Values.Single().InvitedByUserId);
        Assert.IsNull(f.Tenants.Hooks.Context!.AuthUserId); Assert.HasCount(0, f.MembershipCalls);
        var metadata = JsonSerializer.Serialize(f.Jobs.Rows.Values);
        Assert.DoesNotContain("InitialAdminEmail", metadata); Assert.DoesNotContain("enterprise@example.test", metadata);
        Assert.DoesNotContain("Private Enterprise", metadata); Assert.DoesNotContain(f.LastRequest!.Token, metadata);
        Assert.DoesNotContain("enterprise@example.test", JsonSerializer.Serialize(f.Operation.Events));
        Assert.DoesNotContain("Private Enterprise", JsonSerializer.Serialize(f.Operation.Store.Rows.Values));
        Assert.IsTrue(f.LastRequest.RoleIds.SequenceEqual(new[] { f.RoleId }));
    }
    [TestMethod]
    public async Task ChangedIntentConflictsAndDeniedOrInvalidRequestsHaveNoEffects()
    {
        var f = new OnboardingFixture(); f.Allowed = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.CreateAsync(f.Request, "enterprise-intent-01"));
        Assert.HasCount(0, f.Jobs.Rows); f.Allowed = true;
        await Assert.ThrowsAsync<ManagementCustomerIdentityInvalidRequestException>(() => f.Service.CreateAsync(new() { TenantName = "One", InitialAdminEmail = "enterprise@example.test", PolicyKey = "support-*" }, "enterprise-intent-01"));
        await Assert.ThrowsAsync<ManagementCustomerIdentityInvalidRequestException>(() => f.Service.CreateAsync(new() { TenantName = "One", InitialAdminEmail = "Display Name <enterprise@example.test>", PolicyKey = "enterprise-owner" }, "enterprise-intent-01"));
        Assert.HasCount(0, f.Jobs.Rows);
        await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.CreateAsync(new() { TenantName = "Changed", InitialAdminEmail = f.Request.InitialAdminEmail, PolicyKey = f.Request.PolicyKey }, "enterprise-intent-01"));
        Assert.AreEqual(1, f.Tenants.Store.Creates);
    }
    [TestMethod]
    public async Task FailedExtensionsResumeSameTenantAndInviteAfterRestartAndAnotherAssignedOperator()
    {
        var f = new OnboardingFixture(); f.Tenants.Hooks.FailCreated = true;
        var partial = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        Assert.AreEqual("tenant-extensions", partial.Step); Assert.AreEqual("recoverable-failure", partial.Status);
        Assert.AreEqual(0, f.Sends); Assert.HasCount(0, f.InviteRows);
        f.Tenants.Hooks.FailCreated = false; f.Operation.Actor = f.Operation.Actor with { UserId = Guid.NewGuid() };
        var result = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        Assert.AreEqual(partial.TenantId, result.TenantId); Assert.AreEqual(partial.InviteId, result.InviteId);
        Assert.AreEqual(1, f.Tenants.Store.Creates); Assert.AreEqual("awaiting-acceptance", result.Status);
        Assert.AreEqual(f.Jobs.Rows.Values.Single().CreatedByUserId, f.LastRequest!.Actor.UserId);
    }
    [TestMethod]
    public async Task CrashAfterInvitationPersistsReconcilesItsStableIdAndDoesNotSendUntilJournalIsReady()
    {
        var f = new OnboardingFixture(); f.FailAfterInvite = true;
        Assert.AreEqual("recoverable-failure", (await f.Service.CreateAsync(f.Request, "enterprise-intent-01")).Status);
        var invite = f.InviteRows.Values.Single(); Assert.AreEqual(0, f.Sends);
        f.FailAfterInvite = false;
        var done = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        Assert.AreEqual(invite.InviteId, done.InviteId); Assert.AreEqual(1, f.Tenants.Store.Creates);
        Assert.HasCount(1, f.InviteRows); Assert.AreEqual(1, f.Sends);
    }
    [TestMethod]
    public async Task AmbiguousMailNeedsExplicitIdempotentRetryAndNeverRecreatesCustomerResources()
    {
        var f = new OnboardingFixture(); f.FailDelivery = true;
        var first = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        Assert.AreEqual("unknown", first.DeliveryStatus); Assert.AreEqual("recoverable-failure", first.Status);
        await f.Service.CreateAsync(f.Request, "enterprise-intent-01"); Assert.AreEqual(1, f.Sends);
        f.Operation.Actor = f.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingRetry };
        f.FailDelivery = false;
        var retry = await f.Service.RetryAsync(first.OnboardingId, "retry-enterprise-01");
        var again = await f.Service.RetryAsync(first.OnboardingId, "retry-enterprise-01");
        Assert.AreEqual("sent", retry.DeliveryStatus); Assert.AreEqual(retry, again); Assert.AreEqual(2, f.Sends);
        Assert.AreEqual(1, f.Tenants.Store.Creates); Assert.HasCount(1, f.InviteRows);
    }
    [TestMethod]
    public async Task LostDeliveryAcknowledgementStaysUnknownAfterLeaseWithoutAutomaticDuplicateMail()
    {
        var f = new OnboardingFixture(); f.Jobs.FailSent = true;
        Assert.AreEqual("recoverable-failure", (await f.Service.CreateAsync(f.Request, "enterprise-intent-01")).Status);
        f.Jobs.FailSent = false; f.Operation.Clock.Now = f.Operation.Clock.Now.AddMinutes(3);
        var again = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        Assert.AreEqual("unknown", again.DeliveryStatus); Assert.IsFalse(again.Completed); Assert.AreEqual(1, f.Sends);
    }
    [TestMethod]
    public async Task OnlyVerifiedRecipientWithActiveApprovedMembershipCompletesBusinessStatus()
    {
        var f = new OnboardingFixture(); var created = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        var invite = f.InviteRows.Values.Single(); var customer = Guid.NewGuid();
        f.InviteRows[invite.InviteId] = invite with { Status = "redeemed", RedeemedByUserId = customer, RedeemedUtc = DateTimeOffset.UtcNow };
        f.Operation.Actor = f.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingRead };
        Assert.IsFalse((await f.Service.GetAsync(created.OnboardingId, f.Operation.Actor.Operation)).Completed);
        f.Users.Setup(x => x.FindByIdAsync(customer, It.IsAny<CancellationToken>())).ReturnsAsync(new IdentityUser(customer, f.Request.InitialAdminEmail, true));
        f.Memberships.Setup(x => x.GetTenantForUserAsync(customer, created.TenantId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantInfo(created.TenantId!.Value, "Private Enterprise", ["Owner"], true, [f.RoleId]));
        var done = await f.Service.GetAsync(created.OnboardingId, f.Operation.Actor.Operation);
        Assert.IsTrue(done.Completed); Assert.AreEqual("completed", done.Status);
        f.Users.Setup(x => x.FindByIdAsync(customer, It.IsAny<CancellationToken>())).ReturnsAsync(new IdentityUser(customer, "wrong@example.test", true));
        Assert.IsFalse((await f.Service.GetAsync(created.OnboardingId, f.Operation.Actor.Operation)).Completed);
    }
    [TestMethod]
    public async Task JournalIsScopedAuthenticatedAndDoesNotRequireCurrentRolePolicyForReadOnlyStatus()
    {
        var f = new OnboardingFixture(); var created = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        f.Operation.Actor = f.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingRead };
        f.Policy.Policies["enterprise-owner"] = "Admin";
        Assert.IsFalse((await f.Service.GetAsync(created.OnboardingId, f.Operation.Actor.Operation)).Completed);
        var actor = f.Operation.Actor;
        f.Operation.Actor = actor with { ApplicationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<ManagementCustomerIdentityNotFoundException>(() => f.Service.GetAsync(created.OnboardingId, f.Operation.Actor.Operation));
        f.Operation.Actor = actor;
        var row = f.Jobs.Rows.Values.Single(); f.Jobs.Rows[(row.Scope, row.OnboardingId)] = row with { EncryptedInput = row.EncryptedInput[..^4] + "AAAA" };
        await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Service.GetAsync(created.OnboardingId, f.Operation.Actor.Operation));
    }
    [TestMethod]
    public async Task RemovedOrChangedPolicyCannotBeUsedToRetryAndMissingLocalRoleNeverGrantsAnything()
    {
        var f = new OnboardingFixture(); f.FailDelivery = true;
        var created = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        f.Operation.Actor = f.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingRetry };
        f.Policy.Policies["enterprise-owner"] = "Admin";
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RetryAsync(created.OnboardingId, "retry-enterprise-01"));
        Assert.AreEqual(1, f.Sends);
        var missing = new OnboardingFixture(); missing.LocalRole = false;
        var result = await missing.Service.CreateAsync(missing.Request, "enterprise-intent-01");
        Assert.AreEqual("recoverable-failure", result.Status); Assert.AreEqual("administrator-role-unavailable", result.ErrorCode);
        Assert.HasCount(0, missing.InviteRows); Assert.AreEqual(0, missing.Sends);
    }
    [TestMethod]
    public async Task KnownPreSendFailureIsDistinctAndReadOnlyPollingNeverSendsOrChangesJournal()
    {
        var f = new OnboardingFixture(); f.FailBeforeSender = true;
        var created = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        Assert.AreEqual("failed", created.DeliveryStatus); Assert.AreEqual("delivery-not-sent", created.ErrorCode);
        var before = f.Jobs.Rows.Values.Single();
        f.Operation.Actor = f.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingRead };
        await f.Service.GetAsync(created.OnboardingId, f.Operation.Actor.Operation);
        Assert.AreEqual(before, f.Jobs.Rows.Values.Single()); Assert.AreEqual(0, f.Sends);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RetryAsync(created.OnboardingId, "retry-enterprise-01"));
    }
    [TestMethod]
    public async Task NewRetryKeysCannotResetTheDurableDeliveryAttemptLimit()
    {
        var f = new OnboardingFixture(); f.Operation.Options.MaximumAttempts = 1; f.FailDelivery = true;
        var created = await f.Service.CreateAsync(f.Request, "enterprise-intent-01");
        f.Operation.Actor = f.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingRetry };
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.RetryAsync(created.OnboardingId, "retry-enterprise-01"));
        Assert.AreEqual(1, f.Sends); Assert.AreEqual(1, f.Tenants.Store.Creates); Assert.HasCount(1, f.InviteRows);
    }
}

internal sealed class OnboardingFixture : IManagementCustomerIdentityTargetAccessService
{
    public OperationFixture Operation { get; } = new();
    public TenantAdministrationFixture Tenants { get; } = new();
    public OnboardingStore Jobs { get; } = new();
    public Dictionary<Guid, TenantInviteRecord> InviteRows { get; } = [];
    public Guid RoleId { get; } = Guid.NewGuid();
    public bool Allowed { get; set; } = true;
    public bool FailDelivery { get; set; }
    public bool FailBeforeSender { get; set; }
    public bool FailAfterInvite { get; set; }
    public bool LocalRole { get; set; } = true;
    public int Sends { get; private set; }
    public ExternalTenantInviteRequest? LastRequest { get; private set; }
    public List<Guid> MembershipCalls { get; } = [];
    public Mock<IIdentityUserStore> Users { get; } = new();
    public Mock<ITenantMembershipStore> Memberships { get; } = new();
    public ManagementEnterpriseOnboardingOptions Policy { get; } = new() { Policies = new() { ["enterprise-owner"] = "Owner" } };
    public CreateManagementEnterpriseOnboarding Request => new() { TenantName = "Private Enterprise", InitialAdminEmail = "enterprise@example.test", PolicyKey = "enterprise-owner", ExternalReference = "CRM:12" };
    public IManagementEnterpriseOnboardingService Service { get; }
    public IExternalTenantInviteService External { get; }
    public ITenantInviteStore InviteStore { get; }
    public ITenantRoleStore RoleStore { get; }
    public OnboardingFixture()
    {
        Operation.Actor = Operation.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingCreate };
        var coordinator = new ManagementIdentityOperationService(Operation.Store, Operation, this, Options.Create(Operation.Options), Operation.Clock, new ServiceOperationExecutor());
        var tenants = new ManagementTenantAdministrationService(Tenants.Identity, Tenants.Metadata, coordinator, this, Options.Create(Operation.Options), new ServiceOperationExecutor());
        var roles = new Mock<ITenantRoleStore>();
        RoleStore = roles.Object;
        roles.Setup(x => x.GetRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid id, CancellationToken _) =>
            LocalRole ? new[] { new TenantRole(id, RoleId, "Owner", true, true, DateTimeOffset.UtcNow) } : Array.Empty<TenantRole>());
        var external = new Mock<IExternalTenantInviteService>(); external.SetupGet(x => x.SupportsDurableCreation).Returns(true);
        External = external.Object;
        external.Setup(x => x.EnsureAsync(It.IsAny<ExternalTenantInviteRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((ExternalTenantInviteRequest request, CancellationToken _) => {
            LastRequest = request;
            if (!InviteRows.TryGetValue(request.InviteId, out var row)) {
                row = new(request.InviteId, request.TenantId, "email", request.Email, "hidden", "pending", request.CreatedUtc,
                    Guid.Empty, request.ExpiresUtc, RoleIds: request.RoleIds, RequirePasswordSetup: true, CausationId: request.Actor.OperationId.ToString("D"));
                InviteRows.Add(request.InviteId, row);
            }
            if (FailAfterInvite) throw new ManagementCustomerIdentityUnavailableException();
            return TenantInviteInfo.FromRecord(row);
        });
        external.Setup(x => x.DeliverAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => {
                if (FailBeforeSender) return Task.FromException(new IdentityInviteDeliveryNotSentException());
                Sends++; return FailDelivery ? Task.FromException(new Exception("MAIL-SECRET-CANARY")) : Task.CompletedTask;
            });
        var invites = new Mock<ITenantInviteStore>();
        InviteStore = invites.Object;
        invites.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenant, Guid id, CancellationToken _) => InviteRows.TryGetValue(id, out var row) && row.TenantId == tenant ? row : null);
        Memberships.Setup(x => x.GetTenantForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback((Guid id, Guid _, CancellationToken _) => MembershipCalls.Add(id)).ReturnsAsync((TenantInfo?)null);
        Service = new ManagementEnterpriseOnboardingService(Jobs, coordinator, coordinator, tenants, external.Object, invites.Object,
            roles.Object, Users.Object, Memberships.Object, this, Options.Create(Policy), Options.Create(Operation.Options), Operation.Clock, new ServiceOperationExecutor());
    }
    public Task<ManagementDelegatedActor> RequireAsync(string operation, CancellationToken ct = default)
        => Allowed && ManagementCustomerIdentityPermissionPolicy.RequiredFor(Operation.Actor.Operation).Contains(operation)
            ? Task.FromResult(Operation.Actor with { Operation = operation }) : Task.FromException<ManagementDelegatedActor>(new ManagementAccessDeniedException());
}
internal sealed class OnboardingStore : IManagementEnterpriseOnboardingRepository
{
    public Dictionary<(ManagementIdentityOperationScope, Guid), ManagementEnterpriseOnboardingEntity> Rows { get; } = [];
    public bool FailSent { get; set; }
    public Task<ManagementEnterpriseOnboardingEntity?> GetAsync(ManagementIdentityOperationScope scope, Guid id, CancellationToken ct = default)
    { lock (Rows) return Task.FromResult(Rows.GetValueOrDefault((scope, id))); }
    public Task<bool> TryCreateAsync(ManagementEnterpriseOnboardingEntity row, CancellationToken ct = default)
    {
        lock (Rows) {
            if (Rows.ContainsKey((row.Scope, row.OnboardingId))) return Task.FromResult(false);
            Rows.Add((row.Scope, row.OnboardingId), row with { Version = Guid.NewGuid().ToString() }); return Task.FromResult(true);
        }
    }
    public Task<ManagementEnterpriseOnboardingEntity> ReplaceAsync(ManagementEnterpriseOnboardingEntity row, string version, CancellationToken ct = default)
    {
        lock (Rows) {
            if (FailSent && row.DeliveryStatus == "sent") throw new Exception("JOURNAL-SECRET-CANARY");
            if (!Rows.TryGetValue((row.Scope, row.OnboardingId), out var old) || old.Version != version) throw new ManagementIdentityOperationConflictException();
            Rows[(row.Scope, row.OnboardingId)] = row = row with { Version = Guid.NewGuid().ToString() }; return Task.FromResult(row);
        }
    }
}
