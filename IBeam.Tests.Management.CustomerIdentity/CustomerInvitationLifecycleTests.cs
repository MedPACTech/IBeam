using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Exceptions;
using IBeam.Management.CustomerIdentity;
using IBeam.Management;
using IBeam.Management.Identity.Target.Services.Invitations;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerInvitationLifecycleTests
{
    [TestMethod]
    public async Task ResendRotatesOneInviteAndSameKeyDoesNotSendTwiceOrExpandIntent()
    {
        var f = new InvitationLifecycleFixture(); var original = f.Row;
        var first = await f.Service.ResendAsync(f.Tenant, f.Id, "resend-first-intent", original.Version!);
        var repeat = await f.Service.ResendAsync(f.Tenant, f.Id, "resend-first-intent", original.Version!);
        Assert.AreEqual(first.Operation, repeat.Operation); Assert.AreEqual("sent", first.Status); Assert.AreEqual(1, f.Sends);
        Assert.AreNotEqual(original.TokenHash, f.Row.TokenHash); Assert.AreEqual(original.NormalizedDestination, f.Row.NormalizedDestination);
        Assert.IsTrue(original.RoleIds!.SequenceEqual(f.Row.RoleIds!)); Assert.AreEqual(original.InvitedByUserId, f.Row.InvitedByUserId);
        Assert.DoesNotContain(f.LastToken!, JsonSerializer.Serialize(first)); Assert.DoesNotContain(f.LastToken!, JsonSerializer.Serialize(f.Journal.Rows));
        Assert.DoesNotContain(original.NormalizedDestination, JsonSerializer.Serialize(f.Data.Data.Operation.Events));
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.RevokeAsync(f.Tenant, f.Id, "resend-first-intent", original.Version!));
        await Assert.ThrowsAsync<ManagementCustomerIdentityPreconditionException>(() => f.Service.RevokeAsync(f.Tenant, f.Id, "revoke-stale-intent", original.Version!));
        Assert.AreEqual(1, f.Sends);
    }
    [TestMethod]
    public async Task RevokePreservesRolesAndNeverCreatesCustomerActorOrSendsMail()
    {
        var f = new InvitationLifecycleFixture(); var old = f.Row;
        var result = await f.Service.RevokeAsync(f.Tenant, f.Id, "revoke-first-intent", old.Version!);
        Assert.AreEqual("revoked", result.Status); Assert.AreEqual(TenantInviteStatuses.Revoked, f.Row.Status); Assert.IsNull(f.Row.RevokedByUserId);
        Assert.AreEqual(0, f.Sends); Assert.IsTrue(old.RoleIds!.SequenceEqual(f.Row.RoleIds!));
        var repeat = await f.Service.RevokeAsync(f.Tenant, f.Id, "revoke-first-intent", old.Version!); Assert.AreEqual(result.Operation, repeat.Operation);
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.ResendAsync(f.Tenant, f.Id, "resend-revoked-intent", f.Row.Version!));
    }
    [TestMethod]
    public async Task ServerRateLimitsAndRolePolicyApplyToNewRequestsButNeverBreakIdempotentReplay()
    {
        var f = new InvitationLifecycleFixture(); var version = f.Row.Version!;
        await f.Service.ResendAsync(f.Tenant, f.Id, "resend-first-intent", version);
        await Assert.ThrowsAsync<ManagementCustomerIdentityRateLimitException>(() => f.Service.ResendAsync(f.Tenant, f.Id, "resend-second-intent", f.Row.Version!));
        await f.Service.ResendAsync(f.Tenant, f.Id, "resend-first-intent", version); Assert.AreEqual(1, f.Sends);
        f.Data.Data.Operation.Clock.Now = f.Data.Data.Operation.Clock.Now.AddMinutes(2);
        f.Policy.MaximumResendsPerDay = 1;
        await Assert.ThrowsAsync<ManagementCustomerIdentityRateLimitException>(() => f.Service.ResendAsync(f.Tenant, f.Id, "resend-third-intent", f.Row.Version!));
        var other = new InvitationLifecycleFixture(); other.Policy.ApprovedRoleNames = ["Owner"];
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => other.Service.ResendAsync(other.Tenant, other.Id, "resend-role-denied", other.Row.Version!));
        Assert.AreEqual(0, other.Sends);
    }
    [TestMethod]
    [DataRow(true)] [DataRow(false)]
    public async Task DeliveryFailureIsExplicitAndNeverAutomaticallyRetried(bool preparationFailure)
    {
        var f = new InvitationLifecycleFixture(); f.DeliveryFailure = preparationFailure ? new IdentityInviteDeliveryNotSentException() : new Exception("PRIVATE-SMTP-DETAIL");
        var version = f.Row.Version!; var first = await f.Service.ResendAsync(f.Tenant, f.Id, "resend-failure-intent", version);
        Assert.AreEqual(preparationFailure ? "failed" : "unknown", first.Status);
        f.DeliveryFailure = null; await f.Service.ResendAsync(f.Tenant, f.Id, "resend-failure-intent", version);
        Assert.AreEqual(1, f.Sends); Assert.DoesNotContain("PRIVATE-SMTP", JsonSerializer.Serialize(first));
    }
    [TestMethod]
    public async Task LostNativeAndDeliveryAcknowledgementsRecoverWithoutDuplicateSendOrInvitation()
    {
        foreach (var loseDelivery in new[] { false, true }) {
            var f = new InvitationLifecycleFixture(); f.FailAfterNative = !loseDelivery; f.Journal.FailSent = loseDelivery;
            var version = f.Row.Version!;
            await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Service.ResendAsync(f.Tenant, f.Id, "resend-crash-intent", version));
            f.FailAfterNative = false; f.Journal.FailSent = false; f.Data.Data.Operation.Clock.Now = f.Data.Data.Operation.Clock.Now.AddMinutes(3);
            var recovered = await f.Service.ResendAsync(f.Tenant, f.Id, "resend-crash-intent", version);
            Assert.AreEqual(loseDelivery ? "unknown" : "sent", recovered.Status); Assert.AreEqual(1, f.Sends); Assert.HasCount(1, f.Journal.Rows);
        }
    }
    [TestMethod]
    public async Task ClaimedAcceptanceAndLivePermissionLossFenceEverySupportMutation()
    {
        var f = new InvitationLifecycleFixture(); f.Row = f.Row with { AcceptanceId = Guid.NewGuid(), AcceptanceUserId = Guid.NewGuid() };
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.RevokeAsync(f.Tenant, f.Id, "revoke-claimed-intent", f.Row.Version!));
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.ResendAsync(f.Tenant, f.Id, "resend-claimed-intent", f.Row.Version!));
        f.Data.Allowed = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.RevokeAsync(f.Tenant, f.Id, "revoke-denied-intent", f.Row.Version!));
        Assert.AreEqual(0, f.Sends); Assert.HasCount(0, f.Journal.Rows);
    }
    [TestMethod]
    public async Task OtherTenantDisabledTenantAndLegacyAccessGrantsCannotBeResent()
    {
        var f = new InvitationLifecycleFixture();
        await Assert.ThrowsAsync<ManagementCustomerIdentityNotFoundException>(() => f.Service.ResendAsync(Guid.NewGuid(), f.Id, "resend-other-tenant", f.Row.Version!));
        f.Data.Data.Tenants.Store.Rows[f.Tenant] = f.Data.Data.Tenants.Store.Rows[f.Tenant] with { Tenant = new IdentityTenant(f.Tenant, "Private", Status: "Disabled") };
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.ResendAsync(f.Tenant, f.Id, "resend-disabled-tenant", f.Row.Version!));
        var result = await f.Service.RevokeAsync(f.Tenant, f.Id, "revoke-disabled-tenant", f.Row.Version!); Assert.AreEqual("revoked", result.Status);
        var legacy = new InvitationLifecycleFixture(); legacy.Row = legacy.Row with { AccessGrants = [new("records", "private-resource", "Read")] };
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => legacy.Service.ResendAsync(legacy.Tenant, legacy.Id, "resend-legacy-grant", legacy.Row.Version!));
        var revoked = await legacy.Service.RevokeAsync(legacy.Tenant, legacy.Id, "revoke-legacy-grant", legacy.Row.Version!); Assert.AreEqual("revoked", revoked.Status);
        Assert.AreEqual(0, legacy.Sends);
    }
}

internal sealed class InvitationLifecycleFixture : IManagedTenantInviteStore, IManagedTenantInviteDeliveryService, IManagementCustomerInvitationReadRepository
{
    public ProvisioningFixture Data { get; } = new();
    public Guid Tenant => Data.Tenant;
    public Guid Id { get; } = Guid.NewGuid();
    public TenantInviteRecord Row { get; set; }
    public int Sends { get; private set; }
    public string? LastToken { get; private set; }
    public Exception? DeliveryFailure { get; set; }
    public bool FailAfterNative { get; set; }
    public InvitationTransitionJournal Journal { get; } = new();
    public ManagementCustomerInvitationOptions Policy { get; } = new() { ApprovedRoleNames = ["Member"] };
    public Mock<ITenantInviteStore> Native { get; } = new();
    public IManagementCustomerInvitationService Service { get; }
    public InvitationLifecycleFixture()
    {
        Data.Data.Operation.Actor = Data.Data.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.InvitesManage };
        Row = new(Id, Tenant, "email", "customer@example.test", "old-token-hash", TenantInviteStatuses.Pending,
            Data.Data.Operation.Clock.Now, Guid.Empty, Data.Data.Operation.Clock.Now.AddDays(7), RoleIds: [Data.Role]) { Version = "invite-v1" };
        Native.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid tenant, Guid id, CancellationToken _) => tenant == Tenant && id == Id ? Row : null);
        var executor = new ServiceOperationExecutor();
        var receipts = new ManagementIdentityOperationService(Data.Data.Operation.Store, Data.Data.Operation, Data, Options.Create(Data.Data.Operation.Options), Data.Data.Operation.Clock, executor);
        Service = new ManagementCustomerInvitationService(Journal, receipts, Native.Object, this, this, this, Data, Data.Data.Tenants.Store,
            Data, Options.Create(Policy), Options.Create(Data.Data.Operation.Options), Data.Data.Operation.Clock, executor);
    }
    public Task<TenantInviteRecord> BeginAcceptanceAsync(Guid tenant, Guid id, string hash, Guid user, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TenantInviteRecord> TransitionAsync(ManagedTenantInviteTransition request, CancellationToken ct = default)
    {
        if (Row.LastTransitionOperationId == request.OperationId) return Task.FromResult(Row);
        if (Row.Version != request.ExpectedVersion || Row.AcceptanceUserId is not null) throw new IdentityConcurrencyException();
        Row = Row with { Version = Guid.NewGuid().ToString(), LastTransitionOperationId = request.OperationId,
            TokenHash = request.Kind == "resend" ? request.TokenHash! : Row.TokenHash,
            Status = request.Kind == "revoke" ? TenantInviteStatuses.Revoked : TenantInviteStatuses.Pending,
            ExpiresUtc = request.ExpiresUtc ?? Row.ExpiresUtc, LastResendUtc = request.Kind == "resend" ? request.NowUtc : Row.LastResendUtc,
            ResendWindowUtc = request.ResendWindowUtc, ResendCount = request.ResendCount };
        if (FailAfterNative) throw new Exception("PRIVATE-NATIVE-ACK"); return Task.FromResult(Row);
    }
    public Task DeliverAsync(Guid tenant, Guid invite, Guid operation, string token, CancellationToken ct = default)
    { Sends++; LastToken = token; if (DeliveryFailure is not null) throw DeliveryFailure; return Task.CompletedTask; }
    public string SourceId => "invitation-lifecycle-fixture";
    public Task<ManagementCustomerReadPage<ManagementCustomerInvitationReadEntity>> ReadPageAsync(Guid tenant, int size, string? cursor, CancellationToken ct = default)
        => Task.FromResult(new ManagementCustomerReadPage<ManagementCustomerInvitationReadEntity>(tenant == Tenant ? [Projection()] : [], null));
    public Task<ManagementCustomerInvitationReadEntity?> FindAsync(Guid tenant, Guid id, CancellationToken ct = default)
        => Task.FromResult<ManagementCustomerInvitationReadEntity?>(tenant == Tenant && id == Id ? Projection() : null);
    private ManagementCustomerInvitationReadEntity Projection() => new(Tenant, Id, "email", Row.NormalizedDestination, null, null, Row.Status.ToLowerInvariant(),
        string.Join(',', Row.RoleIds ?? []), "[]", Row.CreatedUtc, Row.ExpiresUtc, Row.SentUtc, Row.RedeemedUtc, Row.RevokedUtc, Row.Version!);
}
internal sealed class InvitationTransitionJournal : IManagementCustomerInvitationTransitionRepository
{
    public Dictionary<Guid, ManagementCustomerInvitationTransitionEntity> Rows { get; } = [];
    public bool FailSent { get; set; }
    public Task<ManagementCustomerInvitationTransitionEntity?> GetAsync(ManagementTenantAdministrationScope scope, Guid tenant, Guid operation, CancellationToken ct = default)
        => Task.FromResult(Rows.TryGetValue(operation, out var row) && row.Scope == scope && row.TenantId == tenant ? row : null);
    public Task<bool> TryCreateAsync(ManagementCustomerInvitationTransitionEntity row, CancellationToken ct = default)
        => Task.FromResult(Rows.TryAdd(row.OperationId, row with { Version = Guid.NewGuid().ToString() }));
    public Task<ManagementCustomerInvitationTransitionEntity> ReplaceAsync(ManagementCustomerInvitationTransitionEntity row, string version, CancellationToken ct = default)
    { if (FailSent && row.DeliveryStatus == "sent") throw new Exception("PRIVATE-JOURNAL-ACK");
      if (!Rows.TryGetValue(row.OperationId, out var old) || old.Version != version) throw new ManagementIdentityOperationConflictException();
      Rows[row.OperationId] = row = row with { Version = Guid.NewGuid().ToString() }; return Task.FromResult(row); }
}
