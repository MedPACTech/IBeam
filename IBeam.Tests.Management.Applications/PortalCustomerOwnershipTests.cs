using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class PortalCustomerOwnershipTests
{
    private static readonly string[] Grants=[ManagementCustomerIdentityOperations.OwnersRead,ManagementCustomerIdentityOperations.OwnersTransfer];
    private static Task<Guid> App(PortalHttpFixture f,string key="qurvia",string[]? grants=null,bool installed=true)
        => f.Directories.AddApp(f,key,grants??Grants,includeOwnership:installed);
    private static string Path(PortalHttpFixture f,Guid app)=>$"/api/management/applications/{app}/tenants/{f.Directories.Tenant}/ownership";
    private static async Task<HttpResponseMessage> Write(PortalHttpFixture f,Guid app,string version="ownership-v1",string token="reader",bool extra=false)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,Path(f,app)+"/transfers");
        request.Headers.Add("Authorization","Bearer "+token);request.Headers.Add("Idempotency-Key","portal-owner-intent");request.Headers.TryAddWithoutValidation("If-Match",version);
        request.Content=extra?JsonContent.Create(new {sourceUserId=f.Directories.User,destinationUserId=f.Directories.DestinationOwner,sourceVersion="source-v1",destinationVersion="destination-v1",roleIds=new[]{Guid.NewGuid()}}):
            JsonContent.Create(new TransferManagementCustomerOwnership { SourceUserId=f.Directories.User,DestinationUserId=f.Directories.DestinationOwner,SourceVersion="source-v1",DestinationVersion="destination-v1" });
        return await f.Client.SendAsync(request);
    }
    [TestMethod]
    public async Task ScopedReadAndTransferUseIndependentGrantsAndSafeMetadata()
    {
        await using var f=await PortalHttpFixture.CreateAsync(install:false,customerOwnership:true,audit:true);var app=await App(f);
        using var read=await f.Get(Path(f,app));Assert.AreEqual(HttpStatusCode.OK,read.StatusCode);Assert.DoesNotContain(CustomerDirectoryBackend.Canary,await read.Content.ReadAsStringAsync());
        using var response=await Write(f,app);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode,await response.Content.ReadAsStringAsync());
        Assert.IsNotNull(response.Headers.Location);Assert.AreEqual(1,f.Directories.OwnershipWrites);
        using var retry=await Write(f,app);Assert.AreEqual(HttpStatusCode.OK,retry.StatusCode);Assert.AreEqual(1,f.Directories.OwnershipWrites);
        Assert.DoesNotContain(CustomerDirectoryBackend.Canary,JsonSerializer.Serialize(f.Audit.Rows));
        var onlyRoles=await App(f,"bindry",[ManagementCustomerIdentityOperations.RolesAssign]);
        using var denied=await Write(f,onlyRoles);Assert.AreEqual(HttpStatusCode.Forbidden,denied.StatusCode);
        using var extra=await Write(f,app,extra:true);Assert.AreEqual(HttpStatusCode.BadRequest,extra.StatusCode);
    }
    [TestMethod]
    [DataRow("after-commit-timeout")] [DataRow("after-commit-malformed")] [DataRow("after-commit-503")]
    public async Task LostReplyPollingDoesNotRepeatTransferAndReferencesRemainPersonalAndAppBound(string failure)
    {
        await using var f=await PortalHttpFixture.CreateAsync(install:false,customerOwnership:true);var app=await App(f);f.Directories.Failure=failure;
        using var response=await Write(f,app);Assert.AreEqual(HttpStatusCode.Accepted,response.StatusCode,await response.Content.ReadAsStringAsync());
        Assert.Contains("\"unknown\"",await response.Content.ReadAsStringAsync());Assert.AreEqual(1,f.Directories.OwnershipWrites);
        f.Directories.Failure=null;
        using var poll=await f.Get(response.Headers.Location!.ToString());Assert.AreEqual(HttpStatusCode.OK,poll.StatusCode,await poll.Content.ReadAsStringAsync());
        Assert.Contains("\"succeeded\"",await poll.Content.ReadAsStringAsync());Assert.AreEqual(1,f.Directories.OwnershipWrites);
        var other=await App(f,"bindry");
        using var cross=await f.Get(response.Headers.Location.ToString().Replace(app.ToString(),other.ToString()));Assert.AreEqual(HttpStatusCode.BadRequest,cross.StatusCode);
        using var person=await f.Get(response.Headers.Location.ToString(),"other");Assert.IsTrue(person.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest);
    }
    [TestMethod]
    public async Task RevokedAssignmentAndPostWriteRevocationFailClosed()
    {
        await using var f=await PortalHttpFixture.CreateAsync(install:false,customerOwnership:true);var app=await App(f);
        using var denied=await Write(f,app,token:"other");Assert.AreEqual(HttpStatusCode.Forbidden,denied.StatusCode);Assert.AreEqual(0,f.Directories.OwnershipWrites);
        var identity=(f.Data.Access.Org,app,f.Data.Access.Reader);var old=f.Data.Access.Access.Rows[identity];
        f.Directories.DuringMutation=_=>{f.Data.Access.Access.Rows[identity]=old with {Permissions=[]};return Task.CompletedTask;};
        using var revoked=await Write(f,app);Assert.AreEqual(HttpStatusCode.Forbidden,revoked.StatusCode);Assert.AreEqual(1,f.Directories.OwnershipWrites);
    }
    [TestMethod]
    public async Task UnsupportedTargetsStaleVersionsAndWrongScopeNeverProduceSuccess()
    {
        await using var f=await PortalHttpFixture.CreateAsync(install:false,customerOwnership:true);var app=await App(f);
        using var stale=await Write(f,app,"stale");Assert.AreEqual(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        f.Directories.Failure="owner-scope";using var read=await f.Get(Path(f,app));Assert.AreEqual(HttpStatusCode.BadGateway,read.StatusCode);
        f.Directories.Failure=null;var old=await App(f,"bindry",installed:false);
        using var unsupported=await Write(f,old);Assert.IsTrue(unsupported.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0,f.Directories.OwnershipWrites);
    }
}
