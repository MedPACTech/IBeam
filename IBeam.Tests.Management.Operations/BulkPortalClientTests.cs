using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Services.Abstractions;

namespace IBeam.Tests.Management.Operations;

public sealed partial class RegisteredTransportTests
{
    public sealed class BulkStubState
    { public ConcurrentDictionary<Guid, (ManagementOperationContext Scope, ManagementBulkInvitationRevokeResult Result)> Receipts { get; } = new(); }
    public sealed class BulkStubHandler(BulkStubState state) : IManagementOperationHandler<ManagementBulkInvitationRevokeRequest, ManagementBulkInvitationRevokeResult>, IManagementOperationReceiptHandler<ManagementBulkInvitationRevokeResult>
    {
        public Task<ManagementBulkInvitationRevokeResult> ExecuteAsync(ManagementOperationContext scope, ManagementBulkInvitationRevokeRequest request, CancellationToken ct = default)
        {
            var value = state.Receipts.GetOrAdd(scope.OperationId, _ => (scope, new(scope.TenantId, scope.ResourceId, scope.OperationId, "accepted", "succeeded", request.Invitations.Length, true,
                request.Invitations.Select(x => new ManagementBulkInvitationItemResult(x.InviteId, ManagementBulkInvitationRevokeOperation.ItemOperationId(scope.OperationId, x.InviteId), "revoked", null)).ToArray())));
            if (value.Scope != scope) throw new ManagementOperationConflictException(); return Task.FromResult(value.Result);
        }
        public Task<ManagementBulkInvitationRevokeResult> ReadReceiptAsync(ManagementOperationContext scope, CancellationToken ct = default)
        {
            if (!state.Receipts.TryGetValue(scope.OperationId, out var found) || (found.Scope with { ResourceVersion = null }) != (scope with { ResourceVersion = null })) throw new ManagementAccessDeniedException();
            return Task.FromResult(found.Result with { Outcome = "found" });
        }
    }
    [TestMethod]
    public async Task BulkPortalClientUsesTheTypedSenderAndReadonlyOriginalReceipt()
    {
        await using var f = await Fixture.Create(includeBulkContract: true); var service = new ManagementPortalBulkInvitationRevokeService(f.Client, new ServiceOperationExecutor());
        var request = new ManagementBulkInvitationRevokeRequest([new(Guid.NewGuid(), "invite-version")]); var batch = Guid.NewGuid();
        var result = await service.ExecuteAsync(f.AppId, f.Tenant, batch, request, "bulk-intent", "tenant-version");
        Assert.AreEqual("succeeded", result.State); Assert.AreEqual(batch, result.BatchId); Assert.IsTrue(result.Complete); Assert.AreEqual(1, f.WriteCount);
        var receipt = await service.ReadReceiptAsync(f.AppId, f.Tenant, batch, result.OperationId); Assert.AreEqual("found", receipt.Outcome); Assert.AreEqual(1, f.WriteCount);
    }
    [TestMethod]
    [DataRow("operation")] [DataRow("item-intent")] [DataRow("selection")] [DataRow("reason")]
    public async Task BulkPortalClientRejectsContradictoryMetadataAndPreservesTheOriginalUncertainId(string changed)
    {
        await using var f = await Fixture.Create(includeBulkContract: true); var request = new ManagementBulkInvitationRevokeRequest([new(Guid.NewGuid(), "version")]); var batch = Guid.NewGuid();
        f.AfterSend = async (wire, response) => {
            if (wire.Method != HttpMethod.Post) return response;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(); var value = json.GetProperty("data").Deserialize<ManagementBulkInvitationRevokeResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var item = value.Items[0];
            var bad = changed switch { "operation" => value with { OperationId = Guid.NewGuid() },
                "item-intent" => value with { Items = [item with { OperationId = Guid.NewGuid() }] },
                "selection" => value with { Items = [item with { InviteId = batch, OperationId = ManagementBulkInvitationRevokeOperation.ItemOperationId(value.OperationId, batch) }] },
                _ => value with { Items = [item with { FailureCode = "PRIVATE-PROVIDER-TEXT" }] } };
            response.Content.Dispose(); response.Content = JsonContent.Create(new { success = true, data = bad }); return response;
        };
        var error = await Assert.ThrowsAsync<ManagementOperationUncertainException>(() => f.Client.ExecuteAsync<ManagementBulkInvitationRevokeRequest, ManagementBulkInvitationRevokeResult>(f.AppId,
            ManagementBulkInvitationRevokeOperation.Name, f.Tenant, batch, request, "same-original-intent", "tenant-version"));
        var proof = new JwtSecurityTokenHandler().ReadJwtToken(f.LastWrite!.Assertion); Assert.AreEqual(proof.Claims.Single(x => x.Type == "operation_id").Value, error.OperationId.ToString("D"));
        Assert.DoesNotContain("PRIVATE-", error.ToString()); Assert.AreEqual(1, f.WriteCount); f.AfterSend = null;
        var receipt = await f.Client.ReadReceiptAsync<ManagementBulkInvitationRevokeResult>(f.AppId, ManagementBulkInvitationRevokeOperation.Name, f.Tenant, batch, error.OperationId);
        Assert.AreEqual("succeeded", receipt.State); Assert.AreEqual(1, f.WriteCount);
    }
    [TestMethod]
    public async Task BulkPortalClientDoesNotBorrowHumanInvitesManagePermission()
    {
        await using var f = await Fixture.Create(includeBulkContract: true); f.Assignment = f.Assignment with { Permissions = [ManagementCustomerIdentityOperations.InvitesManage] };
        var service = new ManagementPortalBulkInvitationRevokeService(f.Client, new ServiceOperationExecutor());
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.ExecuteAsync(f.AppId, f.Tenant, Guid.NewGuid(), new([new(Guid.NewGuid(), "version")]), "bulk-intent", "tenant-version"));
        Assert.AreEqual(0, f.WriteCount); Assert.AreEqual(0, f.SecretReads);
    }
}
