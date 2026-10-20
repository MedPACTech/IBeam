using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Azure.Data.Tables;
using IBeam.Identity.Api.DependencyInjection;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Schema;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Identity.Target.Api;
using IBeam.Management.Identity.Target.Services.Delegation;
using IBeam.Management.Identity.Target.Services.Tenants;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using Microsoft.IdentityModel.Tokens;
using IBeam.Management.API.Hosting;

namespace IBeam.Management.API.Demo;

public sealed record DemoCustomerDefinition(string ApplicationKey, Guid ApplicationId, Guid RealmId, Guid CredentialId,
    string MachineKey, string TablePrefix, string Url, string JwtKey, string ProtectionKey, Guid TenantId, Guid DisabledTenantId);

/// <summary>Separate, explicit local native Identity host. Never registers customer Identity in the support container.</summary>
public static class DemoCustomerTarget
{
    public static DemoCustomerDefinition[] Definitions(IConfiguration config) => config.GetSection("ManagementHost:CustomerDemo:Targets").Get<DemoCustomerDefinition[]>() ?? [];
    public static async Task RunAsync(IConfiguration parent, IHostEnvironment environment, string applicationKey, bool setup)
    {
        DevelopmentDemoDiagnostics.Validate(parent, environment);
        var definition = Definitions(parent).Single(x => x.ApplicationKey == applicationKey);
        var endpoint = new Uri(definition.Url);
        if (!endpoint.IsLoopback || endpoint.Scheme != "http" || definition.RealmId == Guid.Empty || definition.ApplicationId == Guid.Empty ||
            !definition.TablePrefix.StartsWith("DemoCustomer", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid isolated customer fixture.");
        var connection = HostRegistration.StorageConnection(parent);
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        b.Logging.ClearProviders(); b.WebHost.UseUrls(definition.Url);
        var values = new Dictionary<string,string?> {
            ["IBeam:Identity:AzureTable:StorageConnectionString"] = connection,
            ["IBeam:AccessControl:AzureTable:StorageConnectionString"] = connection,
            ["IBeam:AccessControl:AzureTable:TablePrefix"] = definition.TablePrefix,
            ["IBeam:Identity:AzureTable:TablePrefix"] = definition.TablePrefix,
            ["IBeam:Identity:AzureTable:CreateTablesIfNotExists"] = setup.ToString(),
            ["IBeam:Identity:Jwt:SigningKey"] = definition.JwtKey,
            ["IBeam:Identity:Jwt:Issuer"] = "synthetic-" + applicationKey,
            ["IBeam:Identity:Jwt:Audience"] = "synthetic-" + applicationKey,
            ["IBeam:Identity:Otp:HashSalt"] = definition.JwtKey,
            ["IBeam:Identity:Otp:VerificationTokenSecret"] = definition.JwtKey,
            ["IBeam:Identity:Invites:Links:DefaultAcceptUrl"] = "https://customer.demo.invalid/invites/accept",
            ["IBeam:Identity:Invites:Links:AllowedOrigins:0"] = "https://customer.demo.invalid",
            ["IBeam:Management:Diagnostics:ApplicationKey"] = applicationKey,
            ["IBeam:Management:Diagnostics:Environment"] = "Testing",
            ["IBeam:Management:CustomerIdentity:IdentityRealmId"] = definition.RealmId.ToString(),
            ["IBeam:Management:CustomerIdentity:DedicatedApplicationRealm"] = "true",
            ["IBeam:Management:CustomerIdentity:Delegation:TrustedPortals:0:OrganizationId"] = parent["IBeam:Management:Identity:OrganizationId"],
            ["IBeam:Management:CustomerIdentity:Delegation:TrustedPortals:0:ApplicationId"] = definition.ApplicationId.ToString(),
            ["IBeam:Management:CustomerIdentity:Delegation:TrustedPortals:0:Issuer"] = "synthetic-support",
            ["IBeam:Management:CustomerIdentity:Delegation:TrustedPortals:0:Audience"] = "synthetic-" + applicationKey,
            ["IBeam:Management:CustomerIdentity:TenantDirectory:ProtectionKey"] = definition.ProtectionKey,
            ["IBeam:Management:CustomerIdentity:UserDirectory:ProtectionKey"] = definition.ProtectionKey,
            ["IBeam:Management:CustomerIdentity:InvitationDirectory:ProtectionKey"] = definition.ProtectionKey,
            ["IBeam:Management:CustomerIdentity:Operations:FingerprintKey"] = definition.ProtectionKey,
            ["IBeam:Management:CustomerIdentity:Onboarding:Policies:enterprise-owner"] = "Owner",
            ["IBeam:Management:CustomerIdentity:Provisioning:Policies:member:0"] = "Member",
            ["IBeam:Management:CustomerIdentity:Provisioning:Policies:administrator:0"] = "Owner",
            ["IBeam:Management:CustomerIdentity:Invitations:ApprovedRoleNames:0"] = "Member",
            ["IBeam:Management:CustomerIdentity:Invitations:ApprovedRoleNames:1"] = "Owner",
            ["IBeam:Management:CustomerIdentity:Memberships:AdministratorRoleNames:0"] = "Owner"
        };
        var enabled=ManagementCustomerIdentityOperations.All.Where(ManagementCustomerIdentityPermissionPolicy.IsAssignable).ToArray();
        for(var index=0;index<enabled.Length;index++) values["IBeam:Management:CustomerIdentity:EnabledOperations:"+index] = enabled[index];
        b.Configuration.AddInMemoryCollection(values);
        b.Services.AddSingleton<IIdentityCommunicationSender>(new CustomerOutbox(Path.Combine(HostRegistration.DataDirectory(parent), applicationKey, "outbox")));
        b.Services.AddIBeamIdentityApiWithCommunicationSender(b.Configuration);
        var protection=b.Services.AddDataProtection().SetApplicationName("IBeam.CustomerDemo."+definition.RealmId)
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(HostRegistration.DataDirectory(parent),applicationKey,"protection-keys")));
        if(OperatingSystem.IsWindows())protection.ProtectKeysWithDpapi();
        b.Services.AddIBeamIdentityTenantAccessEnforcementApi();
        b.Services.AddCors(options=>options.AddPolicy("CustomerDemo",policy=>policy.WithOrigins(parent.GetSection("ManagementHost:Cors:AllowedOrigins").Get<string[]>()??[])
            .WithMethods("GET","POST","PATCH","PUT","DELETE").WithHeaders("Authorization","Content-Type","Accept","If-Match","Idempotency-Key").WithExposedHeaders("Location","ETag")));
        b.Services.AddScoped<ITenantExtensionCoordinator, CustomerExtensions>();
        var tables = new Dictionary<string, TableClient>();
        TableClient Table(string name) => tables.TryGetValue(name, out var table) ? table : tables[name] = new TableClient(connection, definition.TablePrefix + name);
        b.Services.AddSingleton<IManagementAuditRepository>(new AzureTableManagementAuditRepository(Table("ManagementAudit")));
        b.Services.AddScoped<IManagementPrincipalProvider,TargetPrincipals>();
        b.Services.AddSingleton<IManagementApiCredentialRepository>(new AzureTableManagementApiCredentialRepository(Table("ManagementKeys")));
        b.Services.AddSingleton<IManagementDelegationReplayStore>(new AzureTableManagementDelegationReplayStore(Table("ManagementNonces")));
        b.Services.AddSingleton<IManagementIdentityOperationRepository>(new AzureTableManagementIdentityOperationRepository(Table("ManagementOperations")));
        b.Services.AddSingleton<IManagementIdentityOperationAuditRepository>(new AzureTableManagementIdentityOperationAuditRepository(Table("ManagementOperationAudit")));
        b.Services.AddSingleton<IManagementTenantAdministrationRepository>(new AzureTableManagementTenantAdministrationRepository(Table("ManagementTenants")));
        b.Services.AddSingleton<IManagementEnterpriseOnboardingRepository>(new AzureTableManagementEnterpriseOnboardingRepository(Table("ManagementOnboarding")));
        b.Services.AddSingleton<IManagementCustomerProvisioningRepository>(new AzureTableManagementCustomerProvisioningRepository(Table("ManagementProvisioning")));
        b.Services.AddSingleton<IManagementCustomerInvitationTransitionRepository>(new AzureTableManagementCustomerInvitationTransitionRepository(Table("ManagementInvitationTransitions")));
        b.Services.AddSingleton<IManagementActorValidationKeyProvider>(new DemoActorKeys(parent));
        b.Services.AddSingleton<IManagementCustomerTenantReadRepository>(new AzureTableManagementCustomerTenantReadRepository(Table("Tenants")));
        b.Services.AddSingleton<IManagementCustomerUserReadRepository>(new AzureTableManagementCustomerUserReadRepository(Table("TenantUsers")));
        b.Services.AddSingleton<IManagementCustomerInvitationReadRepository>(new AzureTableManagementCustomerInvitationReadRepository(Table("TenantInvites")));
        b.Services.AddSingleton<IManagementAssignableRoleReadRepository>(new AzureTableManagementAssignableRoleReadRepository(Table("Roles")));
        b.Services.AddIBeamManagementCustomerIdentityTargetApi(b.Configuration);
        b.Services.AddIBeamManagementDelegatedActors(b.Configuration);
        b.Services.AddIBeamManagementTenantDirectoryApi(b.Configuration);
        b.Services.AddIBeamManagementTenantProvenance();
        b.Services.AddIBeamManagementCustomerUserDirectoryApi(b.Configuration);
        b.Services.AddIBeamManagementCustomerInvitationDirectoryApi(b.Configuration);
        b.Services.AddIBeamManagementEnterpriseOnboardingApi(b.Configuration);
        b.Services.AddIBeamManagementCustomerProvisioningApi(b.Configuration);
        b.Services.AddIBeamManagementCustomerInvitationsApi(b.Configuration);
        b.Services.AddIBeamManagementCustomerMembershipApi(b.Configuration);
        b.Services.AddIBeamManagementTenantStatusApi(b.Configuration);
        b.Services.AddControllers().ConfigureApplicationPartManager(parts=>parts.FeatureProviders.Add(new CustomerControllerBoundary()));
        await using var app = b.Build();
        if(setup) {
            await using var scope = app.Services.CreateAsyncScope(); var services=scope.ServiceProvider;
            await services.GetRequiredService<IIdentitySchemaManager>().ApplyAsync();
            foreach(var name in new[]{"AccessGrants","PermissionRoleMaps","ServiceOperationPermissions"}) await Table(name).CreateIfNotExistsAsync();
            foreach(var table in tables.Values) await table.CreateIfNotExistsAsync();
            var marker=Path.Combine(HostRegistration.DataDirectory(parent),applicationKey,"seed.complete");
            if(File.Exists(marker)){Console.WriteLine("Existing synthetic customer seed preserved.");return;}
            var keys = services.GetRequiredService<IManagementApiCredentialRepository>();
            if(await keys.GetAsync(applicationKey,"Testing",definition.CredentialId) is null) await keys.CreateAsync(new ManagementApiCredentialEntity {
                CredentialId=definition.CredentialId,ApplicationKey=applicationKey,Environment="Testing",SecretHash=new ManagementCredentialSecretCodec().Hash(definition.MachineKey),
                CreatedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddDays(90),Capabilities=ManagementCustomerIdentityOperations.All });
            var users=services.GetRequiredService<IIdentityUserStore>();
            var email="customer@"+applicationKey+".example.test";
            var user=await users.FindByEmailAsync(email);
            if(user is null) { var result=await users.CreateAsync(new(email,null,parent["ManagementHost:CustomerDemo:CustomerPassword"] ?? throw new InvalidOperationException("Private customer password missing."))); user=result.User ?? throw new InvalidOperationException("Synthetic user creation failed."); await users.SetEmailConfirmedAsync(user.UserId,true); await users.SetTwoFactorAsync(user.UserId,true,"email"); }
            var tenants=services.GetRequiredService<IIdentityTenantStore>(); var roles=services.GetRequiredService<ITenantRoleStore>();
            if(await tenants.FindByIdAsync(definition.TenantId) is null) {
                await tenants.CreateAsync(new(definition.TenantId,"Synthetic self-service workspace"));
                await roles.EnsureTenantMembershipAndGrantRolesAsync(new(definition.TenantId,user.UserId,RoleNames:["Owner"],SetAsDefault:true,UserEmail:email));
                await services.GetRequiredService<IManagementTenantProvenanceRecorder>().RecordSelfServiceAsync(definition.TenantId,definition.TenantId);
            }
            await services.GetRequiredService<IManagementTenantProvenanceRecorder>().RecordSelfServiceAsync(definition.TenantId,definition.TenantId);
            var memberRole=(await roles.GetRolesAsync(definition.TenantId)).SingleOrDefault(x=>x.Name=="Member") ?? await roles.CreateRoleAsync(definition.TenantId,"Member");
            var disabledEmail="disabled@"+applicationKey+".example.test";
            var disabled=await users.FindByEmailAsync(disabledEmail);
            if(disabled is null){disabled=(await users.CreateAsync(new(disabledEmail,null,parent["ManagementHost:CustomerDemo:CustomerPassword"]!))).User ?? throw new InvalidOperationException("Synthetic user creation failed.");await users.SetEmailConfirmedAsync(disabled.UserId,true);await users.SetTwoFactorAsync(disabled.UserId,true,"email");}
            var memberships=services.GetRequiredService<ITenantMembershipStore>();
            if(await memberships.GetTenantForUserAsync(disabled.UserId,definition.TenantId) is null){
                await roles.EnsureTenantMembershipAndGrantRolesAsync(new(definition.TenantId,disabled.UserId,RoleIds:[memberRole.RoleId],UserEmail:disabledEmail));
                await memberships.DisableTenantMembershipAsync(definition.TenantId,disabled.UserId,"Synthetic fixture");
            }
            var pendingId=new Guid(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("synthetic-pending:"+definition.RealmId)).AsSpan(0,16));
            if(await services.GetRequiredService<ITenantInviteStore>().GetAsync(definition.TenantId,pendingId) is null){
                using var supportManifest=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(HostRegistration.DataDirectory(parent),"demo-manifest.json")));
                var created=DateTimeOffset.UtcNow;var token=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));var external=services.GetRequiredService<IExternalTenantInviteService>();
                await external.EnsureAsync(new(){TenantId=definition.TenantId,InviteId=pendingId,Email="pending@"+applicationKey+".example.test",Token=token,
                    RoleIds=[memberRole.RoleId],ApprovedRoleNames=["Member"],CreatedUtc=created,ExpiresUtc=created.AddDays(7),
                    Actor=new(Guid.Parse(parent["IBeam:Management:Identity:OrganizationId"]!),
                        supportManifest.RootElement.GetProperty("users").GetProperty("operator@example.test").GetGuid(),definition.ApplicationId,pendingId)});
                await external.DeliverAsync(definition.TenantId,pendingId,pendingId,token);
            }
            if(await tenants.FindByIdAsync(definition.DisabledTenantId) is null) {
                await tenants.CreateAsync(new(definition.DisabledTenantId,"Synthetic disabled enterprise"));
                await roles.EnsureDefaultRolesAsync(definition.DisabledTenantId);await roles.CreateRoleAsync(definition.DisabledTenantId,"Member");
                await tenants.SetStatusAsync(definition.DisabledTenantId,"Disabled");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);await File.WriteAllTextAsync(marker,"Seed completed; preserve existing status/credentials.");
            Console.WriteLine("Synthetic customer schema/seed ready; existing passwords, status and credentials preserved."); return;
        }
        app.Use(async(context,next)=>{context.Response.Headers.CacheControl="no-store";try{await next();}catch(Exception error){ReportFailure(error);if(context.Response.HasStarted){context.Abort();return;}context.Response.StatusCode=503;await context.Response.WriteAsJsonAsync(new{success=false,errors=new[]{new{code="Demo.Unavailable",message="Synthetic customer service unavailable."}}});}});
        app.UseRouting();app.UseCors("CustomerDemo");app.UseAuthentication();app.UseAuthorization();app.MapControllers();
        app.MapGet("/health",()=>new{service="IBeam.CustomerDemo",applicationKey,identityRealmId=definition.RealmId,dataMode="synthetic-demo"});
        await app.RunAsync();
    }
    private static void ReportFailure(Exception error)
    {
        for(var current=error;current is not null;current=current.InnerException){
            var calls=new System.Diagnostics.StackTrace(current,false).GetFrames()?.Select(x=>x.GetMethod()).Where(x=>x?.DeclaringType?.Namespace?.StartsWith("IBeam.",StringComparison.Ordinal)==true).Take(4).Select(x=>x!.DeclaringType!.Name+"."+x.Name)??[];
            var option=current is Microsoft.Extensions.Options.OptionsValidationException validation ? " options="+validation.OptionsType.Name : "";
            Console.Error.WriteLine("Synthetic request failed: "+current.GetType().Name+option+" calls="+string.Join(",",calls));
        }
    }
    private sealed class TargetPrincipals(IHttpContextAccessor context):IManagementPrincipalProvider
    {public System.Security.Claims.ClaimsPrincipal GetPrincipal()=>context.HttpContext?.User ?? new();}
    private sealed class CustomerExtensions(ITenantRoleStore roles) : ITenantExtensionCoordinator
    {
        public async Task OnTenantCreatedAsync(IdentityTenant tenant,TenantExtensionContext context,CancellationToken ct=default) {await roles.EnsureDefaultRolesAsync(tenant.TenantId,ct);if(!(await roles.GetRolesAsync(tenant.TenantId,ct)).Any(x=>x.Name=="Member")) await roles.CreateRoleAsync(tenant.TenantId,"Member",ct:ct);}
        public Task OnTenantUpdatedAsync(IdentityTenant tenant,IdentityTenant? previous,TenantExtensionContext context,CancellationToken ct=default)=>Task.CompletedTask;
        public Task OnTenantActivatedAsync(IdentityTenant tenant,TenantExtensionContext context,CancellationToken ct=default)=>Task.CompletedTask;
        public Task OnTenantDeactivatedAsync(IdentityTenant tenant,TenantExtensionContext context,CancellationToken ct=default)=>Task.CompletedTask;
        public Task EnsureExtensionAsync(IdentityTenant tenant,TenantExtensionContext context,CancellationToken ct=default)=>Task.CompletedTask;
    }
    private sealed class CustomerOutbox(string directory):IIdentityCommunicationSender
    {public async Task SendAsync(IdentitySenderMessage message,CancellationToken ct=default){Directory.CreateDirectory(directory);await File.WriteAllTextAsync(Path.Combine(directory,Guid.NewGuid().ToString("N")+".json"),JsonSerializer.Serialize(new {message.Destination,message.Code,message.Body,message.ExpiresAt,Purpose=message.Purpose?.ToString(),CreatedUtc=DateTimeOffset.UtcNow}),ct);}}
}
public sealed class DemoActorKeys : IManagementActorSigningKeyProvider,IManagementActorValidationKeyProvider,IDisposable
{
    private readonly RSA key=RSA.Create();
    public DemoActorKeys(IConfiguration config){key.ImportFromPem(File.ReadAllText(Path.Combine(HostRegistration.DataDirectory(config),"actor-signing.pem")));}
    public SigningCredentials GetSigningCredentials()=>new(new RsaSecurityKey(key){KeyId="synthetic-demo"},SecurityAlgorithms.RsaSha256);
    public IReadOnlyList<SecurityKey> GetTrustedKeys(string issuer)=>issuer=="synthetic-support"?[new RsaSecurityKey(key.ExportParameters(false)){KeyId="synthetic-demo"}]:[];
    public void Dispose()=>key.Dispose();
}
