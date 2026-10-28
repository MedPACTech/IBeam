using System.Net;
using System.Text.Json;
using IBeam.Identity.Events;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
[TestCategory("StorageIntegration")]
public sealed class MembershipOutboxSupportHttpTests
{
    [TestMethod]
    public async Task RealSignedSupportMutationCapturesTheVerifiedActorAndItsReplayCreatesNoNewEvent()
    {
        await using var data = await MembershipStorageFixture.Create(membershipEvents: true);
        await using var f = await DelegationFixture.Start(memberships: data);
        var store = new AzureTableIdentityMembershipEventOutboxStore(data.Storage, Options.Create(data.NativeOptions));
        var scope = new IdentityMembershipEventScope("atlas", "Testing", data.NativeOptions.MembershipEventsRealmId, data.Tenant);
        var current = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        var path = $"/api/management-identity/tenants/{data.Tenant:D}/users/{data.Member:D}/membership";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { isEnabled = false }); const string key = "event-support-original";
        var proof = await f.Issue(ManagementCustomerIdentityOperations.MembershipsManage, "PATCH", path, bytes, key, current.Version);
        using var changed = await f.Send(proof, "PATCH", path, bytes, key, current.Version); Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
        var page = await store.ReadPageAsync(scope, 0, 10); Assert.HasCount(1, page.Events); var evt = page.Events[0];
        Assert.AreEqual("membership-disabled", evt.Kind); Assert.AreEqual(IdentityMembershipActorKind.Support, evt.Actor.Kind);
        Assert.AreEqual(f.User, evt.Actor.UserId); Assert.AreEqual(f.Organization, evt.Actor.ManagementOrganizationId); Assert.AreEqual(f.Application, evt.Actor.ManagementApplicationId);
        Assert.AreNotEqual(data.Member, evt.Actor.UserId); Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(evt));
        using var replay = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.MembershipsManage, "PATCH", path, bytes, key, current.Version), "PATCH", path, bytes, key, current.Version);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode); Assert.HasCount(1, (await store.ReadPageAsync(scope, 0, 10)).Events);
        using var rawReplay = await f.Send(proof, "PATCH", path, bytes, key, current.Version); Assert.AreEqual(HttpStatusCode.Unauthorized, rawReplay.StatusCode);
        Assert.AreEqual(evt.EventId, (await store.ReadPageAsync(scope, 0, 10)).Events.Single().EventId);
        Assert.AreEqual(IdentityMembershipActorKind.Unknown, IdentityMembershipEventActorContext.Current.Kind);
    }
}
