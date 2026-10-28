using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Api.DependencyInjection;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tenants;
using IBeam.Identity.Services.Tokens;
using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class OwnershipConsumerCompatibilityTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow(false)] [DataRow(true)]
    public async Task CommittedOwnershipOrDisableInvalidatesIssuedJwtAndRefreshWithoutAffectingOtherTenants(bool disable)
    {
        await using var f=await MembershipStorageFixture.Create(ownershipProtection:true);
        var protection=new IdentityTenantOwnershipProtection(f.Members,f.Roles,
            Options.Create(new IdentityTenantOwnershipOptions {Enabled=true,OwnerRoleNames=[f.Owner.Name]}),new ServiceOperationExecutor());
        await protection.EnsureAsync(f.Tenant);
        var access=new IdentityTenantAccessValidator(f.Members,f.Tenants,new ServiceOperationExecutor());
        var options=new JwtOptions {Issuer="ownership-consumer",Audience="ownership-consumer-api",SigningKey=Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))};
        var sessions=new Dictionary<string,AuthSessionRecord>(); var store=new Mock<IAuthSessionStore>();
        store.Setup(x=>x.SaveAsync(It.IsAny<AuthSessionRecord>(),It.IsAny<CancellationToken>())).Callback((AuthSessionRecord value,CancellationToken _)=>sessions[value.RefreshTokenHash]=value).Returns(Task.CompletedTask);
        store.Setup(x=>x.GetByRefreshTokenHashAsync(It.IsAny<string>(),It.IsAny<CancellationToken>())).ReturnsAsync((string hash,CancellationToken _)=>sessions.GetValueOrDefault(hash));
        store.Setup(x=>x.DeleteByRefreshTokenHashAsync(It.IsAny<string>(),It.IsAny<CancellationToken>())).Callback((string hash,CancellationToken _)=>sessions.Remove(hash)).Returns(Task.CompletedTask);
        var tokens=new JwtTokenService(Options.Create(options),store.Object,Mock.Of<IAuthEventPublisher>(),Mock.Of<IAuthLifecycleHook>(),
            Options.Create(new AuthEventOptions()),NullLogger<JwtTokenService>.Instance,[new IdentityTenantAccessClaimsEnricher(access)]);
        var source=await tokens.CreateAccessTokenAsync(f.AdminA,f.Tenant,[new("role",f.Owner.Name),new("rid",f.Owner.RoleId.ToString("D"))]);
        var destination=await tokens.CreateAccessTokenAsync(f.Member,f.Tenant,[new("role",f.MemberRole.Name),new("rid",f.MemberRole.RoleId.ToString("D"))]);
        var other=await tokens.CreateAccessTokenAsync(f.AdminA,f.OtherTenant,[]);
        var builder=WebApplication.CreateBuilder(); builder.Logging.ClearProviders();builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IIdentityTenantMembershipConditionalStore>(f.Members);builder.Services.AddSingleton<ITenantMembershipStore>(f.Members);
        builder.Services.AddSingleton<IIdentityTenantStore>(f.Tenants);builder.Services.AddSingleton<ITenantRoleStore>(f.Roles);builder.Services.AddSingleton<IServiceOperationExecutor>(new ServiceOperationExecutor());
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(x=>x.TokenValidationParameters=new() {
            ValidateIssuer=true,ValidIssuer=options.Issuer,ValidateAudience=true,ValidAudience=options.Audience,ValidateIssuerSigningKey=true,
            IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),ValidateLifetime=true });
        builder.Services.AddAuthorization();builder.Services.AddIBeamIdentityTenantAccessEnforcementApi();
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();
        app.MapGet("/private",()=>"member").RequireAuthorization();app.MapGet("/owner",()=>"owner").RequireAuthorization(p=>p.RequireRole(f.Owner.Name));
        await app.StartAsync();using var client=app.GetTestClient();
        async Task Check(string jwt,string path,HttpStatusCode expected) {using var message=new HttpRequestMessage(HttpMethod.Get,path);message.Headers.Authorization=new("Bearer",jwt);using var response=await client.SendAsync(message);Assert.AreEqual(expected,response.StatusCode);}
        await Check(source.AccessToken,"/owner",HttpStatusCode.OK);await Check(destination.AccessToken,"/owner",HttpStatusCode.Forbidden);
        var snapshot=await f.Members.ReadOwnershipSnapshotAsync(f.Tenant,5000);var before=snapshot.Members.Single(x=>x.UserId==f.AdminA);
        if(disable) await new IdentityTenantMembershipAdministrationService(f.Members,f.Roles,f.Tenants,new ServiceOperationExecutor(),protection)
            .SetEnabledAsync(f.Tenant,f.AdminA,false,before.Version,Guid.NewGuid(),[f.Owner.RoleId]);
        else {
            var native=new IdentityTenantOwnershipAdministrationService(f.Members,f.Roles,f.Tenants,
                Options.Create(new IdentityTenantOwnershipOptions {Enabled=true,OwnerRoleNames=[f.Owner.Name]}),
                Options.Create(new IdentityTenantOwnershipTransferOptions {Enabled=true,Mode=IdentityTenantOwnerMode.MultipleOwners}),new ServiceOperationExecutor());
            await native.TransferAsync(new(f.Tenant,f.AdminA,f.Member,before.Version,snapshot.Members.Single(x=>x.UserId==f.Member).Version,snapshot.Version,Guid.NewGuid()));
        }
        await Check(source.AccessToken,"/private",HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<IdentityUnauthorizedException>(()=>tokens.RefreshAccessTokenAsync(source.RefreshToken!));
        await Check(other.AccessToken,"/private",HttpStatusCode.OK);_ = await tokens.RefreshAccessTokenAsync(other.RefreshToken!);
        if(!disable) {
            await Check(destination.AccessToken,"/private",HttpStatusCode.Unauthorized);
            await Assert.ThrowsAsync<IdentityUnauthorizedException>(()=>tokens.RefreshAccessTokenAsync(destination.RefreshToken!));
            var refreshed=await tokens.CreateAccessTokenAsync(f.Member,f.Tenant,[new("role",f.Owner.Name),new("rid",f.Owner.RoleId.ToString("D"))]);
            await Check(refreshed.AccessToken,"/owner",HttpStatusCode.OK);
            var former=await tokens.CreateAccessTokenAsync(f.AdminA,f.Tenant,[]);await Check(former.AccessToken,"/owner",HttpStatusCode.Forbidden);
        }
    }
    [TestMethod]
    public void OldContractsRemainCompatibleAndOwnershipGrantsAreNotInferredFromRoleAssignment()
    {
        var legacy=new ManagementCustomerIdentityInfo("consumer","Testing",Guid.NewGuid(),"dedicated-app",1,[ManagementCustomerIdentityOperations.Info,ManagementCustomerIdentityOperations.RolesAssign]);
        Assert.AreEqual(0,legacy.OwnershipTransferProtocolVersion);
        CollectionAssert.AreEqual(new[]{ManagementCustomerIdentityOperations.RolesAssign},ManagementCustomerIdentityPermissionPolicy.RequiredFor(ManagementCustomerIdentityOperations.RolesAssign).ToArray());
        CollectionAssert.AreEqual(new[]{ManagementCustomerIdentityOperations.OwnersTransfer},ManagementCustomerIdentityPermissionPolicy.RequiredFor(ManagementCustomerIdentityOperations.OwnersTransfer).ToArray());
        Assert.IsNotNull(typeof(IdentityTenantMembershipAdministrationService).GetConstructor([typeof(IIdentityTenantMembershipConditionalStore),typeof(ITenantRoleStore),typeof(IIdentityTenantStore),typeof(IServiceOperationExecutor)]));
    }
}
