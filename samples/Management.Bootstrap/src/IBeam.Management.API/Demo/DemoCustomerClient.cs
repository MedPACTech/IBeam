using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.WebUtilities;

namespace IBeam.Management.API.Demo;

/// <summary>Explicit Development-only synthetic loopback port. Production keeps the framework's pinned HTTPS client.</summary>
public sealed class DemoCustomerClient(IConfiguration config,IHostEnvironment environment,IManagementDelegatedActorProvider actors) : IManagementCustomerIdentityClient
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private DemoCustomerDefinition Require(ManagementCustomerIdentityConnection c)
    {
        DevelopmentDemoDiagnostics.Validate(config,environment);
        var target=DemoCustomerTarget.Definitions(config).SingleOrDefault(x=>x.ApplicationId==c.ApplicationId);
        if(target is null || c.OrganizationId!=Guid.Parse(config["IBeam:Management:Identity:OrganizationId"]!) || c.UserId==Guid.Empty ||
            c.IdentityRealmId!=target.RealmId || c.ApplicationKey!=target.ApplicationKey || c.Environment!="Testing" || c.CredentialId!=target.CredentialId ||
            c.BaseUrl!="https://"+target.ApplicationKey[5..]+".demo.invalid" ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(c.Key.Value),Encoding.UTF8.GetBytes(target.MachineKey))) throw new ManagementAccessDeniedException();
        return target;
    }
    public async Task<ManagementCustomerDirectoryPage<T>> ListAsync<T>(ManagementCustomerIdentityConnection c,Guid? tenant,ManagementCustomerDirectoryQuery query,CancellationToken ct=default) where T:class
    {
        var (path,op)=Directory<T>(tenant); var parameters=new Dictionary<string,string?>{["pageSize"]=query.PageSize.ToString(),["continuationToken"]=query.ContinuationToken,["status"]=query.Status,["namePrefix"]=query.NamePrefix};
        using var document=await Send(c,op,"GET",QueryHelpers.AddQueryString(path,parameters),null,null,null,ct);
        var root=document.RootElement;var rows=root.GetProperty("data").Deserialize<T[]>(Json) ?? throw new ManagementCustomerIdentityResponseException();
        if(rows.Length>query.PageSize || query.PageSize is <1 or >100) throw new ManagementCustomerIdentityResponseException();
        foreach(var row in rows) Check(row,tenant,null);
        return new(rows,root.GetProperty("pageSize").GetInt32(),root.TryGetProperty("continuationToken",out var token)?token.GetString():null);
    }
    public async Task<T> FindAsync<T>(ManagementCustomerIdentityConnection c,Guid? tenant,Guid id,CancellationToken ct=default) where T:class
    {var(path,op)=Directory<T>(tenant);var row=await Data<T>(c,op,"GET",path+"/"+id.ToString("D"),null,null,null,ct);Check(row,tenant,id);return row;}
    private static(string,string) Directory<T>(Guid? tenant) where T:class
    {
        if(typeof(T)==typeof(ManagementCustomerTenantDto)&&tenant is null)return("/api/management-identity/tenants",ManagementCustomerIdentityOperations.TenantsRead);
        if(tenant is null || tenant==Guid.Empty)throw new ManagementCustomerIdentityInvalidRequestException();
        return typeof(T)==typeof(ManagementCustomerUserDto)?($"/api/management-identity/tenants/{tenant:D}/users",ManagementCustomerIdentityOperations.UsersRead):
            typeof(T)==typeof(ManagementCustomerInvitationDto)?($"/api/management-identity/tenants/{tenant:D}/invites",ManagementCustomerIdentityOperations.InvitesRead):throw new ManagementCustomerIdentityInvalidRequestException();
    }
    private static void Check<T>(T row,Guid? tenant,Guid? id)
    {if(row switch{ManagementCustomerTenantDto t=>t.TenantId==Guid.Empty||id is not null&&t.TenantId!=id||!ManagementTenantSources.IsValid(t.Source),
        ManagementCustomerUserDto u=>u.TenantId!=tenant||u.UserId==Guid.Empty||id is not null&&u.UserId!=id,
        ManagementCustomerInvitationDto i=>i.TenantId!=tenant||i.InviteId==Guid.Empty||id is not null&&i.InviteId!=id,_=>true})throw new ManagementCustomerIdentityResponseException();}
    public Task<ManagementTenantMutationResult> CreateTenantAsync(ManagementCustomerIdentityConnection c,CreateManagementCustomerTenant body,string key,CancellationToken ct=default)=>Data<ManagementTenantMutationResult>(c,ManagementCustomerIdentityOperations.TenantsCreate,"POST","/api/management-identity/tenants",body,key,null,ct);
    public Task<ManagementTenantMutationResult> RenameTenantAsync(ManagementCustomerIdentityConnection c,Guid tenant,RenameManagementCustomerTenant body,string version,string key,CancellationToken ct=default)=>Data<ManagementTenantMutationResult>(c,ManagementCustomerIdentityOperations.TenantsUpdate,"PATCH",$"/api/management-identity/tenants/{tenant:D}",body,key,version,ct);
    public Task<ManagementTenantMutationResult> SetTenantStatusAsync(ManagementCustomerIdentityConnection c,Guid tenant,bool active,string version,string key,CancellationToken ct=default)=>Data<ManagementTenantMutationResult>(c,ManagementCustomerIdentityOperations.TenantsStatus,"POST",$"/api/management-identity/tenants/{tenant:D}/"+(active?"activate":"deactivate"),new{},key,version,ct);
    public Task<ManagementIdentityOperationDto> CreateOnboardingAsync(ManagementCustomerIdentityConnection c,CreateManagementEnterpriseOnboarding body,string key,CancellationToken ct=default)=>Data<ManagementIdentityOperationDto>(c,ManagementCustomerIdentityOperations.OnboardingCreate,"POST","/api/management-identity/enterprise-onboardings",body,key,null,ct);
    public Task<ManagementIdentityOperationDto> RetryOnboardingAsync(ManagementCustomerIdentityConnection c,Guid id,string key,CancellationToken ct=default)=>Data<ManagementIdentityOperationDto>(c,ManagementCustomerIdentityOperations.OnboardingRetry,"POST",$"/api/management-identity/enterprise-onboardings/{id:D}/retry",new{},key,null,ct);
    public async Task<IReadOnlyList<ManagementCustomerOnboardingPolicyDto>> ListOnboardingPoliciesAsync(ManagementCustomerIdentityConnection c,CancellationToken ct=default)=>await Data<ManagementCustomerOnboardingPolicyDto[]>(c,ManagementCustomerIdentityOperations.OnboardingRead,"GET","/api/management-identity/enterprise-onboardings/policies",null,null,null,ct);
    public async Task<IReadOnlyList<ManagementAssignableRoleDto>> ListAssignableRolesAsync(ManagementCustomerIdentityConnection c,Guid tenant,CancellationToken ct=default)=>await Data<ManagementAssignableRoleDto[]>(c,ManagementCustomerIdentityOperations.RolesRead,"GET",$"/api/management-identity/tenants/{tenant:D}/assignable-roles",null,null,null,ct);
    public Task<ManagementCustomerProvisioningResult> ProvisionUserAsync(ManagementCustomerIdentityConnection c,Guid tenant,ProvisionManagementCustomerUser body,string key,CancellationToken ct=default)=>Data<ManagementCustomerProvisioningResult>(c,ManagementCustomerIdentityOperations.UsersProvision,"POST",$"/api/management-identity/tenants/{tenant:D}/users/provision",body,key,null,ct);
    public Task<ManagementCustomerInvitationResult> ChangeInvitationAsync(ManagementCustomerIdentityConnection c,Guid tenant,Guid invite,bool revoke,string key,string version,CancellationToken ct=default)=>Data<ManagementCustomerInvitationResult>(c,ManagementCustomerIdentityOperations.InvitesManage,"POST",$"/api/management-identity/tenants/{tenant:D}/invites/{invite:D}/"+(revoke?"revoke":"resend"),new{},key,version,ct);
    public Task<ManagementCustomerMembershipResult> SetMembershipAsync(ManagementCustomerIdentityConnection c,Guid tenant,Guid user,ChangeManagementCustomerMembership body,string version,string key,CancellationToken ct=default)=>Data<ManagementCustomerMembershipResult>(c,ManagementCustomerIdentityOperations.MembershipsManage,"PATCH",$"/api/management-identity/tenants/{tenant:D}/users/{user:D}/membership",body,key,version,ct);
    public Task<ManagementCustomerMembershipResult> SetCustomerRolesAsync(ManagementCustomerIdentityConnection c,Guid tenant,Guid user,ChangeManagementCustomerRoles body,string version,string key,CancellationToken ct=default)=>Data<ManagementCustomerMembershipResult>(c,ManagementCustomerIdentityOperations.RolesAssign,"PUT",$"/api/management-identity/tenants/{tenant:D}/users/{user:D}/roles",body,key,version,ct);
    public Task<ManagementIdentityOperationDto> GetOperationAsync(ManagementCustomerIdentityConnection c,Guid id,string authority,CancellationToken ct=default)=>Data<ManagementIdentityOperationDto>(c,authority,"GET",$"/api/management-identity/operations/{id:D}",null,null,null,ct);
    public async Task<ManagementIdentityOperationDto?> ResolveOperationAsync(ManagementCustomerIdentityConnection c,ManagementIdentityOperationLookup body,string key,string authority,CancellationToken ct=default)
    {try{return await Data<ManagementIdentityOperationDto>(c,authority,"POST","/api/management-identity/operations/resolve",body,key,null,ct);}catch(ManagementCustomerIdentityNotFoundException){return null;}}
    private async Task<T> Data<T>(ManagementCustomerIdentityConnection c,string operation,string method,string path,object? body,string? key,string? version,CancellationToken ct)
    {using var json=await Send(c,operation,method,path,body,key,version,ct);return json.RootElement.GetProperty("data").Deserialize<T>(Json)??throw new ManagementCustomerIdentityResponseException();}
    private async Task<JsonDocument> Send(ManagementCustomerIdentityConnection c,string operation,string method,string path,object? body,string? key,string? version,CancellationToken ct)
    {
        var target=Require(c);var info=await Info(target,ct);
        if(info.ApplicationKey!=c.ApplicationKey||info.Environment!=c.Environment||info.IdentityRealmId!=c.IdentityRealmId||info.ProtocolVersion!=1||!info.Capabilities.Contains(operation))throw new ManagementAccessDeniedException();
        var bytes=body is null?Array.Empty<byte>():JsonSerializer.SerializeToUtf8Bytes(body,Json);
        var assertion=await actors.IssueAsync(c.ApplicationId,operation,new(){Method=method,PathAndQuery=path,Body=bytes,IdempotencyKey=key,IfMatch=version},ct);
        return await Raw(target,method,path,bytes,key,version,assertion.Value,ct);
    }
    internal static async Task<ManagementCustomerIdentityInfo> Info(DemoCustomerDefinition target,CancellationToken ct)
    {using var json=await Raw(target,"GET","/api/management-identity/info",[],null,null,null,ct);return json.RootElement.GetProperty("data").Deserialize<ManagementCustomerIdentityInfo>(Json)??throw new ManagementCustomerIdentityResponseException();}
    private static async Task<JsonDocument> Raw(DemoCustomerDefinition target,string method,string path,byte[] bytes,string? key,string? version,string? actor,CancellationToken ct)
    {
        var origin=new Uri(target.Url);if(!origin.IsLoopback||origin.Scheme!="http"||!path.StartsWith("/api/management-identity/",StringComparison.Ordinal))throw new ManagementAccessDeniedException();
        using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false,UseCookies=false};using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(12)};
        using var request=new HttpRequestMessage(new HttpMethod(method),new Uri(origin,path));request.Headers.Add("X-IBeam-Management-Key",target.MachineKey);
        if(actor is not null)request.Headers.Add("X-IBeam-Management-Actor",actor);if(key is not null)request.Headers.Add("Idempotency-Key",key);if(version is not null)request.Headers.TryAddWithoutValidation("If-Match",version);
        if(bytes.Length>0){request.Content=new ByteArrayContent(bytes);request.Content.Headers.ContentType=new("application/json");}
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new ManagementAccessDeniedException();
        if(response.StatusCode==HttpStatusCode.NotFound)throw new ManagementCustomerIdentityNotFoundException();if(response.StatusCode==HttpStatusCode.Conflict)throw new ManagementIdentityOperationConflictException();
        if(response.StatusCode==HttpStatusCode.PreconditionFailed)throw new ManagementCustomerIdentityPreconditionException();if(response.StatusCode==HttpStatusCode.BadRequest)throw new ManagementCustomerIdentityInvalidRequestException();
        if(response.StatusCode==HttpStatusCode.TooManyRequests)throw new ManagementCustomerIdentityRateLimitException();
        if(!response.IsSuccessStatusCode)throw new ManagementCustomerIdentityUnavailableException();
        using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var chunk=new byte[4096];
        while(true){var count=await stream.ReadAsync(chunk,ct);if(count==0)break;buffer.Write(chunk,0,count);if(buffer.Length>262144)throw new ManagementCustomerIdentityResponseException();}
        JsonDocument document;try{document=JsonDocument.Parse(buffer.ToArray(),new(){MaxDepth=16});}catch(JsonException){throw new ManagementCustomerIdentityResponseException();}if(!document.RootElement.TryGetProperty("success",out var success)||success.ValueKind!=JsonValueKind.True){document.Dispose();throw new ManagementCustomerIdentityResponseException();}return document;
    }
}
