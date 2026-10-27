using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class OperationTests
{
    [TestMethod]
    public async Task TenantScopedCreatesReserveNewIdsWhileExistingResourceUpdatesRetainTheirId()
    {
        var f = new OperationFixture(); var tenant = Guid.NewGuid(); var handler = new OperationHandler();
        f.Actor = f.Actor with { Operation = ManagementCustomerIdentityOperations.InvitesManage };
        var first = await f.Service.ExecuteAsync(f.Request(resource: tenant), handler);
        var originalRows = f.Store.Rows.ToArray(); f.Store.Rows.Clear();
        var restored = await f.Service.ExecuteAsync(f.Request(resource: tenant), handler);
        Assert.AreEqual(first.ResourceId, restored.ResourceId); // stable derivation also protects a conditional store after metadata loss
        f.Store.Rows.Clear(); foreach (var entry in originalRows) f.Store.Rows.Add(entry.Key, entry.Value);
        var second = await f.Service.ExecuteAsync(f.Request(key: "another-intent-123", resource: tenant), handler);
        Assert.AreNotEqual(tenant, first.ResourceId); Assert.AreNotEqual(first.ResourceId, second.ResourceId);
        f.Actor = f.Actor with { Operation = ManagementCustomerIdentityOperations.TenantsUpdate };
        var update = await f.Service.ExecuteAsync(f.Request(resource: tenant, existing: tenant), handler);
        Assert.AreEqual(tenant, update.ResourceId);
    }
    [TestMethod]
    public async Task SameCanonicalRequestSurvivesRestartAndChangedRequestConflictsWithoutAnotherWrite()
    {
        var f = new OperationFixture(); var handler = new OperationHandler();
        var first = await f.Service.ExecuteAsync(f.Request("{\"name\":\"private\",\"nested\":{\"b\":2,\"a\":1}}"), handler);
        var retry = await f.Service.ExecuteAsync(f.Request(" { \"nested\": { \"a\":1,\"b\":2 }, \"name\": \"private\" } "), handler);
        Assert.AreEqual(first, retry); Assert.AreEqual("succeeded", retry.Status); Assert.AreEqual(1, handler.Calls);
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.ExecuteAsync(f.Request("{\"name\":\"changed\"}"), handler));
        Assert.HasCount(1, f.Store.Rows); Assert.HasCount(2, f.Events);
    }
    [TestMethod]
    public async Task ConcurrentRequestsShareOneReservationClaimAndStableDownstreamResource()
    {
        var f = new OperationFixture(); var handler = new OperationHandler();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.During = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
        var first = f.Service.ExecuteAsync(f.Request(), handler); await entered.Task;
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Service.ExecuteAsync(f.Request(), handler)));
        Assert.HasCount(1, f.Store.Rows); Assert.IsTrue(concurrent.All(x => x.Status == "running"));
        release.SetResult(); var done = await first;
        Assert.IsTrue(concurrent.All(x => x.OperationId == done.OperationId && x.ResourceId == done.ResourceId)); Assert.AreEqual(1, handler.Calls);
    }
    [TestMethod]
    public async Task ExpiredLeaseCannotOverwriteTheNewOwnerOrDuplicateItsConditionalCustomerWrite()
    {
        var f = new OperationFixture(); var handler = new OperationHandler(); var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.During = async ct => {
            if (Interlocked.Increment(ref calls) == 1) { entered.SetResult(); await release.Task.WaitAsync(ct); }
        };
        var oldOwner = f.Service.ExecuteAsync(f.Request(), handler); await entered.Task;
        f.Clock.Now = f.Clock.Now.AddMinutes(2);
        var newOwner = await f.Service.ExecuteAsync(f.Request(), handler);
        Assert.AreEqual("succeeded", newOwner.Status);
        release.SetResult();
        await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => oldOwner);
        Assert.AreEqual(1, handler.Calls); Assert.AreEqual("succeeded", f.Store.Rows.Values.Single().Status);
    }
    [TestMethod]
    public async Task CancellationPersistsUncertaintyAndPreservesTheCallersCancellationSignal()
    {
        var f = new OperationFixture(); var handler = new OperationHandler { During = ct => Task.Delay(Timeout.Infinite, ct) };
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.ExecuteAsync(f.Request(), handler, cancel.Token));
        Assert.AreEqual("uncertain", f.Store.Rows.Values.Single().Status); Assert.AreEqual(0, handler.Calls);
    }
    [TestMethod]
    [DataRow("reply")] [DataRow("receipt-write")]
    public async Task LostRepliesAndReceiptWritesReconcileTheExistingResourceInsteadOfRepeatingIt(string failure)
    {
        var f = new OperationFixture(); var handler = new OperationHandler { FailAfterWrite = failure == "reply" };
        f.Store.FailFinish = failure == "receipt-write";
        await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Service.ExecuteAsync(f.Request(), handler));
        var old = f.Store.Rows.Values.Single();
        Assert.AreEqual(failure == "reply" ? "uncertain" : "running", old.Status); Assert.AreEqual(1, handler.Calls);
        handler.FailAfterWrite = false; f.Store.FailFinish = false; f.Clock.Now = f.Clock.Now.AddMinutes(2);
        var done = await f.Service.ExecuteAsync(f.Request(), handler);
        Assert.AreEqual("succeeded", done.Status); Assert.AreEqual(old.ResourceId, done.ResourceId); Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual(2, f.Store.Rows.Values.Single().Attempts);
    }
    [TestMethod]
    public async Task UnknownReconciliationDoesNotApplyAndRecoveryAttemptsAreBounded()
    {
        var f = new OperationFixture(); f.Options.MaximumAttempts = 1; var handler = new OperationHandler { Unknown = true };
        var receipt = await f.Service.ExecuteAsync(f.Request(), handler);
        Assert.AreEqual("uncertain", receipt.Status); Assert.AreEqual(0, handler.Calls);
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.ExecuteAsync(f.Request(), handler));
    }
    [TestMethod]
    [DataRow("reservation")] [DataRow("audit")]
    public async Task DurableIntentFailurePreventsAnyCustomerWrite(string failure)
    {
        var f = new OperationFixture(); var handler = new OperationHandler();
        f.Store.FailCreate = failure == "reservation"; f.AuditOutage = failure == "audit";
        await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Service.ExecuteAsync(f.Request(), handler));
        Assert.AreEqual(0, handler.Calls);
        if (failure == "audit") Assert.IsTrue(f.Store.Rows.Values.Single().IntentPending);
    }
    [TestMethod]
    public async Task OutboxRedeliveryAcknowledgesLostDeliveryAndCompletedAuditWithoutRepeatingCustomerChanges()
    {
        var f = new OperationFixture(); var handler = new OperationHandler(); f.Store.FailAcknowledge = true;
        await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Service.ExecuteAsync(f.Request(), handler));
        Assert.HasCount(1, f.Events); Assert.AreEqual(0, handler.Calls);
        f.Store.FailAcknowledge = false; f.FailResults = true;
        await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Service.ExecuteAsync(f.Request(), handler));
        var row = f.Store.Rows.Values.Single(); Assert.AreEqual("succeeded", row.Status); Assert.IsTrue(row.ResultPending);
        f.FailResults = false;
        await f.Service.DeliverAuditAsync(row.OperationId, row.Operation);
        Assert.IsFalse(f.Store.Rows.Values.Single().ResultPending); Assert.HasCount(2, f.Events); Assert.AreEqual(1, handler.Calls);
        var receipt = await f.Service.ExecuteAsync(f.Request(), handler); Assert.AreEqual("succeeded", receipt.Status); Assert.AreEqual(1, handler.Calls);
    }
    [TestMethod]
    public async Task ReceiptsAndAuditNeverPersistRequestPayloadOrIdempotencyKeys()
    {
        var f = new OperationFixture(); var handler = new OperationHandler();
        var result = await f.Service.ExecuteAsync(f.Request(), handler);
        var json = JsonSerializer.Serialize(new { f.Store.Rows.Values, f.Events, result });
        Assert.DoesNotContain("secret-customer@example.test", json); Assert.DoesNotContain("request-12345678", json);
        Assert.DoesNotContain("Fingerprint", json); Assert.DoesNotContain("LeaseId", json);
        Assert.AreEqual(f.Actor.UserId, f.Events.Values.Single(x => x.Succeeded).ActorUserId);
        Assert.AreEqual(f.Actor.CredentialId, f.Events.Values.Single(x => x.Succeeded).CredentialId);
    }
    [TestMethod]
    [DataRow("org")] [DataRow("app")] [DataRow("realm")] [DataRow("authority")] [DataRow("disabled")]
    public async Task OperationReadsRequireCurrentOriginalAuthorityAndExactTargetScope(string change)
    {
        var f = new OperationFixture(); var row = await f.Service.ExecuteAsync(f.Request(), new OperationHandler());
        f.Actor = change switch { "org" => f.Actor with { OrganizationId = Guid.NewGuid() }, "app" => f.Actor with { ApplicationId = Guid.NewGuid() },
            "realm" => f.Actor with { IdentityRealmId = Guid.NewGuid() }, "authority" => f.Actor with { Operation = ManagementCustomerIdentityOperations.TenantsUpdate }, _ => f.Actor };
        if (change == "disabled") f.Allowed = false;
        if (change is "authority" or "disabled")
            await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.GetAsync(row.OperationId, f.Actor.Operation));
        else await Assert.ThrowsAsync<ManagementCustomerIdentityNotFoundException>(() => f.Service.GetAsync(row.OperationId, f.Actor.Operation));
    }
    [TestMethod]
    public async Task OnboardingReadCannotReadUnrelatedMutationReceiptsAndExpiryNeverReusesAnExistingClaim()
    {
        var f = new OperationFixture(); var handler = new OperationHandler(); var request = f.Request();
        var row = await f.Service.ExecuteAsync(request, handler);
        var original = f.Actor; f.Actor = f.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingRead };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.GetAsync(row.OperationId, f.Actor.Operation));
        f.Actor = original; f.Clock.Now = f.Clock.Now.AddDays(91);
        await Assert.ThrowsAsync<ManagementCustomerIdentityNotFoundException>(() => f.Service.GetAsync(row.OperationId, f.Actor.Operation));
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.ExecuteAsync(request, handler));
        Assert.AreEqual(1, handler.Calls);
    }
    [TestMethod]
    public async Task ExplicitOnboardingReadCanPollOnlyAnOnboardingReceiptWithoutNewWriteAuthority()
    {
        var f = new OperationFixture(); f.Actor = f.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingCreate };
        var handler = new OperationHandler(); var done = await f.Service.ExecuteAsync(f.Request(), handler);
        f.Actor = f.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingRead };
        Assert.AreEqual(done, await f.Service.GetAsync(done.OperationId, f.Actor.Operation)); Assert.AreEqual(1, handler.Calls);
    }
    [TestMethod]
    [DataRow("")] [DataRow("short")] [DataRow("bad key value")]
    public async Task MissingOrInvalidIdempotencyKeysCannotReserveWork(string key)
    {
        var f = new OperationFixture(); await Assert.ThrowsAsync<ManagementCustomerIdentityInvalidRequestException>(() => f.Service.ExecuteAsync(f.Request(key: key), new OperationHandler()));
        Assert.HasCount(0, f.Store.Rows);
    }
    [TestMethod]
    [DataRow("{\"a\":1,\"a\":2}")] [DataRow("[]")] [DataRow("invalid")]
    public async Task InvalidOrAmbiguousPayloadsFailBeforeReservation(string payload)
    {
        var f = new OperationFixture(); await Assert.ThrowsAsync<ManagementCustomerIdentityInvalidRequestException>(() => f.Service.ExecuteAsync(f.Request(payload), new OperationHandler()));
        Assert.HasCount(0, f.Store.Rows);
    }
}
