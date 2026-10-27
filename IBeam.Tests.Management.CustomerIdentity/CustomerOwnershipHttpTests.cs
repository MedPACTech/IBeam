using System.Net;
using System.Text.Json;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tenants;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerOwnershipHttpTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Path(MembershipStorageFixture f) => $"/api/management-identity/tenants/{f.Tenant:D}/ownership";
    private static async Task Configure(MembershipStorageFixture f) => await new IdentityTenantOwnershipProtection(f.Members, f.Roles,
        Options.Create(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = [f.Owner.Name] }), new ServiceOperationExecutor()).EnsureAsync(f.Tenant);
    private static async Task<(TransferManagementCustomerOwnership Request,string Version)> Command(MembershipStorageFixture f)
    {
        var snapshot = await f.Members.ReadOwnershipSnapshotAsync(f.Tenant,5000);
        return (new() { SourceUserId = f.AdminA, DestinationUserId = f.Member, SourceVersion = snapshot.Members.Single(x => x.UserId == f.AdminA).Version,
            DestinationVersion = snapshot.Members.Single(x => x.UserId == f.Member).Version }, snapshot.Version);
    }
    private static async Task<HttpResponseMessage> Write(DelegationFixture target, MembershipStorageFixture data,
        TransferManagementCustomerOwnership request,string version,string key="ownership-http-intent")
    {
        var path=Path(data)+"/transfers"; var bytes=JsonSerializer.SerializeToUtf8Bytes(request,Json);
        var proof=await target.Issue(ManagementCustomerIdentityOperations.OwnersTransfer,"POST",path,bytes,key,version);
        return await target.Send(proof,"POST",path,bytes,key,version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task DiscoveryReadAndAtomicTransferAreExplicitScopedAndIdempotentWithSafeAudit()
    {
        await using var data=await MembershipStorageFixture.Create(ownershipProtection:true); await Configure(data);
        var receipts=new OperationStore(); await using var target=await DelegationFixture.Start(memberships:data,operations:receipts,ownershipTransfer:true);
        using var info=await target.Send(null,path:"/api/management-identity/info");
        using var discovery=JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        Assert.AreEqual(1,discovery.RootElement.GetProperty("data").GetProperty("ownershipTransferProtocolVersion").GetInt32());
        using var read=await target.Send(await target.Issue(ManagementCustomerIdentityOperations.OwnersRead,path:Path(data)),path:Path(data));
        Assert.AreEqual(HttpStatusCode.OK,read.StatusCode); Assert.DoesNotContain("PRIVATE-CUSTOMER",await read.Content.ReadAsStringAsync());
        var (request,version)=await Command(data); using var first=await Write(target,data,request,version);
        Assert.AreEqual(HttpStatusCode.OK,first.StatusCode,await first.Content.ReadAsStringAsync()); Assert.IsNotNull(first.Headers.Location);
        var after=(await data.Members.FindStateAsync(data.Tenant,data.AdminA))!.Version;
        using var retry=await Write(target,data,request,version); Assert.AreEqual(HttpStatusCode.OK,retry.StatusCode);
        Assert.AreEqual(after,(await data.Members.FindStateAsync(data.Tenant,data.AdminA))!.Version); Assert.HasCount(1,receipts.Rows);
        using var poll=await target.Send(await target.Issue(ManagementCustomerIdentityOperations.OwnersTransfer,path:first.Headers.Location!.ToString()),path:first.Headers.Location.ToString());
        Assert.AreEqual(HttpStatusCode.OK,poll.StatusCode); Assert.AreEqual(after,(await data.Members.FindStateAsync(data.Tenant,data.AdminA))!.Version);
        Assert.IsTrue(target.Audit.Rows.Any(x=>x.SubjectId==data.AdminA && x.Operation==ManagementCustomerIdentityOperations.OwnersTransfer));
        Assert.IsTrue(target.Audit.Rows.Any(x=>x.SubjectId==data.Member && x.Operation.EndsWith("destination.succeeded",StringComparison.Ordinal)));
        Assert.DoesNotContain("PRIVATE-CUSTOMER",JsonSerializer.Serialize(target.Audit.Rows));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow("source")] [DataRow("destination")] [DataRow("snapshot")]
    public async Task AllStaleVersionsReturnPreconditionFailureWithoutAWrite(string field)
    {
        await using var data=await MembershipStorageFixture.Create(ownershipProtection:true); await Configure(data);
        await using var target=await DelegationFixture.Start(memberships:data,operations:new OperationStore(),ownershipTransfer:true);
        var (request,version)=await Command(data);
        var changed=new TransferManagementCustomerOwnership { SourceUserId=request.SourceUserId,DestinationUserId=request.DestinationUserId,
            SourceVersion=field=="source"?"stale":request.SourceVersion,DestinationVersion=field=="destination"?"stale":request.DestinationVersion };
        using var response=await Write(target,data,changed,field=="snapshot"?"stale":version);
        Assert.AreEqual(HttpStatusCode.PreconditionFailed,response.StatusCode,await response.Content.ReadAsStringAsync());
        Assert.AreEqual(request.SourceVersion,(await data.Members.FindStateAsync(data.Tenant,data.AdminA))!.Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow("source")] [DataRow("destination")] [DataRow("snapshot")] [DataRow("idempotency")] [DataRow("app")] [DataRow("realm")] [DataRow("operation")] [DataRow("path")]
    public async Task SignedTransferCannotBeSubstitutedOrBorrowRoleAssignmentAuthority(string field)
    {
        await using var data=await MembershipStorageFixture.Create(ownershipProtection:true); await Configure(data);
        var receipts=new OperationStore(); await using var target=await DelegationFixture.Start(memberships:data,operations:receipts,ownershipTransfer:true);
        var (command,version)=await Command(data); var path=Path(data)+"/transfers"; var bytes=JsonSerializer.SerializeToUtf8Bytes(command,Json);
        var proof=await target.Issue(field=="operation"?ManagementCustomerIdentityOperations.RolesAssign:ManagementCustomerIdentityOperations.OwnersTransfer,"POST",path,bytes,"ownership-proof-key",version);
        if(field is "app" or "realm") proof=target.Change(proof,p=>p[field]=Guid.NewGuid().ToString("D"));
        if(field is "source" or "destination") bytes=JsonSerializer.SerializeToUtf8Bytes(new TransferManagementCustomerOwnership {
            SourceUserId=command.SourceUserId,DestinationUserId=command.DestinationUserId,SourceVersion=field=="source"?"tampered":command.SourceVersion,
            DestinationVersion=field=="destination"?"tampered":command.DestinationVersion },Json);
        using var response=await target.Send(proof,"POST",field=="path"?path.Replace(data.Tenant.ToString(),Guid.NewGuid().ToString()):path,bytes,
            field=="idempotency"?"different-proof-key":"ownership-proof-key",field=="snapshot"?"tampered":version);
        Assert.IsTrue(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.HasCount(0,receipts.Rows); Assert.AreEqual(command.SourceVersion,(await data.Members.FindStateAsync(data.Tenant,data.AdminA))!.Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow("machine")] [DataRow("policy")]
    public async Task MachineAndTargetPolicyCeilingsRemainIndependent(string ceiling)
    {
        await using var data=await MembershipStorageFixture.Create(ownershipProtection:true); await Configure(data);
        var receipts=new OperationStore(); await using var target=await DelegationFixture.Start(memberships:data,operations:receipts,ownershipTransfer:true);
        var (command,version)=await Command(data); var path=Path(data)+"/transfers"; var body=JsonSerializer.SerializeToUtf8Bytes(command,Json);
        var proof=await target.Issue(ManagementCustomerIdentityOperations.OwnersTransfer,"POST",path,body,"ownership-ceiling",version);
        if(ceiling=="machine") target.Credentials.Row=target.Credentials.Row with { Capabilities=target.Credentials.Row.Capabilities.Where(x=>x!=ManagementCustomerIdentityOperations.OwnersTransfer).ToArray() };
        else { var options=target.App.Services.GetRequiredService<IOptions<ManagementCustomerIdentityTargetOptions>>().Value;
            options.EnabledOperations=options.EnabledOperations.Where(x=>x!=ManagementCustomerIdentityOperations.OwnersTransfer).ToArray(); }
        using var response=await target.Send(proof,"POST",path,body,"ownership-ceiling",version);
        Assert.IsTrue(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden); Assert.HasCount(0,receipts.Rows);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ChangingTheSourceUnderTheSameTenantOwnershipKeyCannotCreateASecondTransfer()
    {
        await using var data=await MembershipStorageFixture.Create(ownershipProtection:true); await Configure(data);
        var receipts=new OperationStore(); await using var target=await DelegationFixture.Start(memberships:data,operations:receipts,ownershipTransfer:true);
        var (first,version)=await Command(data);
        using var completed=await Write(target,data,first,version); Assert.AreEqual(HttpStatusCode.OK,completed.StatusCode);
        var after=await data.Members.ReadOwnershipSnapshotAsync(data.Tenant,5000);
        var changed=new TransferManagementCustomerOwnership { SourceUserId=data.AdminB,DestinationUserId=data.AdminA,
            SourceVersion=after.Members.Single(x=>x.UserId==data.AdminB).Version,DestinationVersion=after.Members.Single(x=>x.UserId==data.AdminA).Version };
        using var conflict=await Write(target,data,changed,after.Version);
        Assert.AreEqual(HttpStatusCode.Conflict,conflict.StatusCode); Assert.HasCount(1,receipts.Rows);
        Assert.AreEqual(after.Version,(await data.Members.ReadOwnershipSnapshotAsync(data.Tenant,5000)).Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task LostJournalReplyKeepsPollingReadOnlyAndExplicitSameIntentRetryReconcilesTheNativeReceipt()
    {
        await using var data=await MembershipStorageFixture.Create(ownershipProtection:true); await Configure(data);
        var receipts=new OperationStore { FailFinish=true };
        await using var target=await DelegationFixture.Start(memberships:data,operations:receipts,ownershipTransfer:true);
        var (request,version)=await Command(data);
        using var failed=await Write(target,data,request,version); Assert.AreEqual(HttpStatusCode.ServiceUnavailable,failed.StatusCode);
        var source=(await data.Members.FindStateAsync(data.Tenant,data.AdminA))!;
        Assert.IsFalse(source.RoleIds.Contains(data.Owner.RoleId));
        var pair=receipts.Rows.Single(); var pollPath="/api/management-identity/operations/"+pair.Value.OperationId;
        using var poll=await target.Send(await target.Issue(ManagementCustomerIdentityOperations.OwnersTransfer,path:pollPath),path:pollPath);
        Assert.AreEqual(HttpStatusCode.OK,poll.StatusCode); Assert.DoesNotContain("\"succeeded\"",await poll.Content.ReadAsStringAsync());
        Assert.AreEqual(source.Version,(await data.Members.FindStateAsync(data.Tenant,data.AdminA))!.Version);
        receipts.FailFinish=false; receipts.Rows[pair.Key]=pair.Value with { LeaseUntilUtc=DateTimeOffset.MinValue };
        using var recovered=await Write(target,data,request,version); Assert.AreEqual(HttpStatusCode.OK,recovered.StatusCode,await recovered.Content.ReadAsStringAsync());
        Assert.AreEqual(source.Version,(await data.Members.FindStateAsync(data.Tenant,data.AdminA))!.Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task OlderTargetDoesNotInstallOrAdvertiseOwnershipTransfer()
    {
        await using var data=await MembershipStorageFixture.Create(); await using var target=await DelegationFixture.Start(memberships:data);
        using var info=await target.Send(null,path:"/api/management-identity/info"); var text=await info.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ownershipTransferProtocolVersion",text); Assert.DoesNotContain(ManagementCustomerIdentityOperations.OwnersTransfer,text);
        using var route=await target.Send(null,path:Path(data)); Assert.AreEqual(HttpStatusCode.NotFound,route.StatusCode);
    }
}
