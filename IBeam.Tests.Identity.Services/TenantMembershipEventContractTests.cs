using System.Text.Json;
using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Services.Tenants;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class TenantMembershipEventContractTests
{
    private readonly IdentityTenantMembershipEventFactory factory = new(new ServiceOperationExecutor());
    private static readonly Guid A = Guid.Parse("11111111-1111-1111-1111-111111111111"), B = Guid.Parse("22222222-2222-2222-2222-222222222222");
    [TestMethod]
    [DataRow("roles-granted")] [DataRow("roles-revoked")] [DataRow("roles-replaced")]
    [DataRow("membership-enabled")] [DataRow("membership-disabled")] [DataRow("membership-changed")]
    [DataRow("membership-linked")] [DataRow("membership-unlinked")]
    public async Task NativeAndSupportCommitsShareTheSameVersionedMembershipShape(string kind)
    {
        var commit = Commit(kind); var native = await factory.ProjectAsync(commit);
        var support = await factory.ProjectAsync(commit with { Actor = new(IdentityMembershipActorKind.Support, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) });
        Assert.IsNotNull(native); Assert.IsNotNull(support); Assert.AreEqual(kind, native.Kind); Assert.AreEqual(native.Kind, support.Kind);
        Assert.AreEqual(commit.EventId, native.EventId); Assert.AreEqual(commit.OperationId, native.OperationId); Assert.AreEqual(commit.Sequence, native.Sequence);
        Assert.AreEqual(native.Scope, support.Scope); Assert.AreEqual(native.EventId, support.EventId);
        Assert.AreEqual(JsonSerializer.Serialize(native.Changes), JsonSerializer.Serialize(support.Changes));
        Assert.IsTrue(native.Changes.All(x => x.UserId != Guid.Empty && (x.Before is null || x.Before.Revision != Guid.Empty) && (x.After is null || x.After.Revision != Guid.Empty)));
    }
    [TestMethod]
    [DataRow(IdentityMembershipCommitDisposition.Replayed)] [DataRow(IdentityMembershipCommitDisposition.NoChange)] [DataRow(IdentityMembershipCommitDisposition.Rejected)]
    public async Task AttemptedRejectedAndReplayedOperationsCreateNoNewLogicalEvent(IdentityMembershipCommitDisposition disposition)
        => Assert.IsNull(await factory.ProjectAsync(Commit("roles-granted") with { Disposition = disposition }));
    [TestMethod]
    public async Task NativeVersionOnlyWritesAndRoleOrderChangesAreNotLogicalStateChanges()
    {
        var c = Commit("roles-granted"); var before = new IdentityMembershipEventState(Guid.NewGuid(), true, [B, A]);
        Assert.IsNull(await factory.ProjectAsync(c with { Changes = [new(c.Changes[0].UserId, before, before with { Revision = Guid.NewGuid(), RoleIds = [A, B] })] }));
    }
    [TestMethod]
    public async Task OwnershipTransferIsOneAtomicEnvelopeContainingBothOriginalAndNewOwners()
    {
        var c = Transfer(); var result = await factory.ProjectAsync(c); Assert.IsNotNull(result);
        Assert.AreEqual("ownership-transferred", result.Kind); Assert.HasCount(2, result.Changes); Assert.AreEqual(c.OperationId, result.OperationId);
        Assert.IsTrue(result.Changes[0].Before!.RoleIds.Contains(A)); Assert.IsFalse(result.Changes[0].After!.RoleIds.Contains(A));
        Assert.IsFalse(result.Changes[1].Before!.RoleIds.Contains(A)); Assert.IsTrue(result.Changes[1].After!.RoleIds.Contains(A));
        Assert.IsTrue(result.Changes.All(x => x.Before!.IsEnabled && x.After!.IsEnabled));
    }
    [TestMethod]
    public async Task TransferMovesTheSourceHeldOwnerRolesWhenTheHostDefinesAdditionalOwnerRoles()
    {
        var result = await factory.ProjectAsync(Transfer() with { ProtectedOwnerRoleIds = [A, B] });
        Assert.IsNotNull(result); Assert.AreEqual("ownership-transferred", result.Kind);
        CollectionAssert.AreEquivalent(new[] { A }, result.Changes[1].After!.RoleIds.ToArray());
    }
    [TestMethod]
    [DataRow("ownerless")] [DataRow("two-sources")] [DataRow("unrelated-role-change")] [DataRow("disabled-destination")]
    public async Task PartialOrUnrelatedTransferSnapshotsCannotBeReportedAsAnAtomicOwnerTransfer(string change)
    {
        var c = Transfer(); var values = c.Changes.ToArray();
        values[1] = change switch {
            "ownerless" => values[1] with { After = values[1].After! with { RoleIds = [] } },
            "two-sources" => values[1] with { Before = values[1].Before! with { RoleIds = [A] }, After = values[1].After! with { RoleIds = [] } },
            "unrelated-role-change" => values[1] with { After = values[1].After! with { RoleIds = [A, B] } },
            _ => values[1] with { After = values[1].After! with { IsEnabled = false } }
        };
        await Assert.ThrowsAsync<IdentityProviderException>(() => factory.ProjectAsync(c with { Changes = values }));
    }
    [TestMethod]
    [DataRow("tenant")] [DataRow("realm")] [DataRow("application")] [DataRow("operation")] [DataRow("sequence")]
    [DataRow("non-utc")] [DataRow("revision")] [DataRow("duplicate-role")] [DataRow("support-actor")] [DataRow("system-actor")]
    public async Task InvalidNativeMetadataIsRejectedWithAFixedProviderError(string change)
    {
        var c = Commit("roles-granted");
        c = change switch {
            "tenant" => c with { Scope = c.Scope with { TenantId = Guid.Empty } },
            "realm" => c with { Scope = c.Scope with { RealmId = Guid.Empty } },
            "application" => c with { Scope = c.Scope with { ApplicationKey = "PRIVATE-PROFILE\n" } },
            "operation" => c with { OperationId = Guid.Empty }, "sequence" => c with { Sequence = 0 },
            "non-utc" => c with { OccurredUtc = c.OccurredUtc.ToOffset(TimeSpan.FromHours(1)) },
            "revision" => c with { Changes = [c.Changes[0] with { After = c.Changes[0].After! with { Revision = c.Changes[0].Before!.Revision } }] },
            "duplicate-role" => c with { Changes = [c.Changes[0] with { After = c.Changes[0].After! with { RoleIds = [A, A] } }] },
            "support-actor" => c with { Actor = new(IdentityMembershipActorKind.Support, Guid.NewGuid(), null, null, null) },
            _ => c with { Actor = new(IdentityMembershipActorKind.System, Guid.NewGuid(), null, null, null) }
        };
        var error = await Assert.ThrowsAsync<IdentityProviderException>(() => factory.ProjectAsync(c)); Assert.DoesNotContain("PRIVATE-", error.ToString());
    }
    [TestMethod]
    public async Task EventProjectionFreezesProviderBuffersAndPreservesOriginalAttribution()
    {
        var c = Transfer(); var roles = new[] { A }; var changes = c.Changes.ToArray();
        changes[0] = changes[0] with { Before = changes[0].Before! with { RoleIds = roles } }; c = c with { Changes = changes };
        var result = (await factory.ProjectAsync(c))!; var json = JsonSerializer.Serialize(result);
        roles[0] = B; changes[0] = changes[1];
        Assert.AreEqual(json, JsonSerializer.Serialize(result)); Assert.AreEqual(c.Actor, result.Actor);
        Assert.Throws<NotSupportedException>(() => ((IList<IdentityMembershipEventChange>)result.Changes).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Guid>)result.Changes[0].Before!.RoleIds).Clear());
    }
    [TestMethod]
    public async Task NewEventsContainNoInviteDestinationMetadataOrUnrestrictedPayloadBag()
    {
        var result = (await factory.ProjectAsync(Commit("roles-replaced")))!;
        Assert.IsFalse(typeof(AuthLifecycleEventBase).IsAssignableFrom(result.GetType()));
        var json = JsonSerializer.Serialize(result); Assert.DoesNotContain("Metadata", json); Assert.DoesNotContain("Destination", json); Assert.DoesNotContain("RoleNames", json);
        Assert.AreEqual("Customer", JsonDocument.Parse(json).RootElement.GetProperty("Actor").GetProperty("Kind").GetString());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TenantMembershipChangedEvent>(json[..^1] + ",\"Metadata\":{\"secret\":\"PRIVATE-CREDENTIAL\"}}"));
        // Existing invite contracts remain usable and their historical destination fields are not silently repurposed.
        var invitation = new TenantInviteAcceptedEvent { InviteId = Guid.NewGuid(), TenantId = result.Scope.TenantId, NormalizedDestination = "PRIVATE-RECIPIENT" };
        Assert.AreEqual(TenantInviteAcceptedEvent.TypeName, invitation.EventType); Assert.DoesNotContain("PRIVATE-", result.ToString());
    }
    [TestMethod]
    public void ContractRegistrationInstallsNoEmitterOutboxOrImplicitNativeDeliveryClaim()
    {
        var services = new ServiceCollection(); services.AddSingleton<IServiceOperationExecutor>(new ServiceOperationExecutor());
        services.AddIBeamIdentityMembershipEventContracts(); services.AddIBeamIdentityMembershipEventContracts();
        using var p = services.BuildServiceProvider(); Assert.IsNotNull(p.GetRequiredService<IIdentityTenantMembershipEventFactory>());
        Assert.IsNull(p.GetService<IIdentityTenantMembershipCommittedEventReader>()); Assert.IsNull(p.GetService<IAuthEventPublisher>());
    }
    private static IdentityMembershipCommittedChange Commit(string kind)
    {
        IdentityMembershipEventState? before = new(Guid.NewGuid(), true, []), after = new(Guid.NewGuid(), true, [A]);
        switch (kind) {
            case "roles-revoked": before = before with { RoleIds = [A] }; after = after with { RoleIds = [] }; break;
            case "roles-replaced": before = before with { RoleIds = [B] }; break;
            case "membership-enabled": before = before with { IsEnabled = false }; after = after with { RoleIds = [] }; break;
            case "membership-disabled": after = after with { IsEnabled = false, RoleIds = [] }; break;
            case "membership-changed": after = after with { IsEnabled = false }; break;
            case "membership-linked": before = null; break;
            case "membership-unlinked": after = null; break;
        }
        return new(IdentityMembershipCommitDisposition.Committed, Guid.NewGuid(), new("fixture", "Testing", Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(), 12, DateTimeOffset.UtcNow, new(IdentityMembershipActorKind.Customer, Guid.NewGuid(), null, null, Guid.NewGuid()),
            [new(Guid.NewGuid(), before, after)], []);
    }
    private static IdentityMembershipCommittedChange Transfer()
    {
        var c = Commit("roles-revoked");
        return c with { Changes = [c.Changes[0], new(Guid.NewGuid(), new(Guid.NewGuid(), true, []), new(Guid.NewGuid(), true, [A]))], ProtectedOwnerRoleIds = [A] };
    }
}
