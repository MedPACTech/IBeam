using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Management.Entities;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Management.Models;
using IBeam.Management.Api;
using IBeam.Management.API.Hosting;

namespace IBeam.Management.API.Demo;

/// <summary>Terminating trusted local command; never mapped to HTTP.</summary>
public static class CustomerDemoInstallation
{
    public static async Task ConfigureAsync(IServiceProvider provider,IConfiguration config,IHostEnvironment environment)
    {
        DevelopmentDemoDiagnostics.Validate(config,environment);
        var marker=Path.Combine(HostRegistration.DataDirectory(config),"customer-installation.complete");
        if(File.Exists(marker)){Console.WriteLine("Customer connection setup already completed; changed assignments/keys remain unchanged.");return;}
        await using var scope=provider.CreateAsyncScope();var services=scope.ServiceProvider;
        var apps=services.GetRequiredService<IManagementApplicationRepository>();var credentials=services.GetRequiredService<IManagementApplicationCredentialRepository>();
        var secrets=services.GetRequiredService<IManagementSecretStore>();var assignments=services.GetRequiredService<IManagementApplicationAccessRepository>();
        var org=Guid.Parse(config["IBeam:Management:Identity:OrganizationId"]!);
        using var manifest=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(HostRegistration.DataDirectory(config),"demo-manifest.json")));
        var diagnosticKeys=JsonSerializer.Deserialize<Dictionary<string,string>>(await File.ReadAllTextAsync(Path.Combine(HostRegistration.DataDirectory(config),"demo-keys.json")))!;
        foreach(var definition in DemoCustomerTarget.Definitions(config)){
            var app=await apps.GetAsync(org,definition.ApplicationId)??throw new InvalidOperationException("Synthetic app is missing.");
            if(!app.IsEnabled){Console.WriteLine("Disabled synthetic application preserved.");continue;}
            if(app.ActiveCredentialId is not Guid previousId){Console.WriteLine("Disconnected synthetic app preserved.");continue;}
            var previous=await credentials.GetAsync(org,app.ApplicationId,previousId);
            if(previous is null){Console.WriteLine("Missing synthetic connection preserved.");continue;}
            var info=await DemoCustomerClient.Info(definition,CancellationToken.None);
            if(info.ApplicationKey!=definition.ApplicationKey||info.IdentityRealmId!=definition.RealmId||info.Environment!="Testing")throw new InvalidOperationException("Synthetic realm mismatch.");
            var existing=await credentials.GetAsync(org,app.ApplicationId,definition.CredentialId);
            if(existing is null){
                var reference=await secrets.CreateAsync(new(org,app.ApplicationId),new(definition.MachineKey));
                var capabilities=info.Capabilities.Concat(previous.Capabilities.Where(x=>x is ManagementOperations.DiagnosticsInfo or ManagementOperations.ErrorsRead or ManagementOperations.LogsRead)).Distinct().ToArray();
                await credentials.CreateAsync(new(){OrganizationId=org,ApplicationId=app.ApplicationId,CredentialId=definition.CredentialId,SecretReference=reference,
                    BaseUrl=app.BaseUrl,VerifiedUtc=DateTimeOffset.UtcNow,CreatedBy="trusted-synthetic-setup",Capabilities=capabilities,CustomerIdentity=new("available",info)});
            }
            if(app.ActiveCredentialId!=definition.CredentialId)await apps.ReplaceAsync(app with{ActiveCredentialId=definition.CredentialId,UpdatedUtc=DateTimeOffset.UtcNow,UpdatedBy="trusted-synthetic-setup"},app.Version);
            diagnosticKeys[definition.ApplicationKey]=definition.MachineKey;
            foreach(var person in manifest.RootElement.GetProperty("users").EnumerateObject()){
                if(person.Name is not ("operator@example.test" or "reader@example.test"))continue;
                var user=person.Value.GetGuid();var current=await assignments.GetAsync(org,app.ApplicationId,user);
                if(current is null || current.RevokedUtc is not null)continue;
                await assignments.ReplaceAsync(current with{Permissions=current.Permissions.Concat(ManagementCustomerIdentityOperations.All.Where(ManagementCustomerIdentityPermissionPolicy.IsAssignable)).Distinct().ToArray()},current.Version);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(HostRegistration.DataDirectory(config),"demo-keys.json"),JsonSerializer.Serialize(diagnosticKeys));
        await File.WriteAllTextAsync(marker,"Synthetic customer connections initialized. Reruns preserve access changes.");
        Console.WriteLine("Synthetic customer connections configured through trusted tooling; no browser machine credentials.");
    }
    public static void Register(IServiceCollection services,IConfiguration config,IHostEnvironment environment)
    {
        if(!config.GetValue<bool>("ManagementHost:CustomerIdentity:Enabled"))return;
        if(config.GetValue<bool>("ManagementHost:CustomerDemo:Enabled")){
            DevelopmentDemoDiagnostics.Validate(config,environment);
            services.AddScoped<IManagementCustomerIdentityClient,DemoCustomerClient>();
            services.AddScoped<IManagementTargetProfileProbe,CustomerProbe>();
        }
        services.AddSingleton<IManagementActorSigningKeyProvider,DemoActorKeys>();
        services.AddIBeamManagementActorDelegation(config);
        services.AddIBeamManagementPortalCustomerDirectoriesApi(config);
        services.AddIBeamManagementPortalIdentityOperationsApi(config);
        services.AddIBeamManagementPortalTenantAdministrationApi(config);
        services.AddIBeamManagementPortalEnterpriseOnboardingApi(config);
        services.AddIBeamManagementPortalCustomerProvisioningApi(config);
        services.AddIBeamManagementPortalCustomerMembershipApi(config);
        services.AddIBeamManagementPortalTenantStatusApi(config);
    }
    private sealed class CustomerProbe(DevelopmentDemoDiagnostics diagnostics,IConfiguration config):IManagementTargetProfileProbe
    {
        public async Task<ManagementTargetProfile> ProbeProfileAsync(string origin,ManagementSecretValue key,CancellationToken ct=default)
        {
            var definition=DemoCustomerTarget.Definitions(config).SingleOrDefault(x=>origin=="https://"+x.ApplicationKey[5..]+".demo.invalid");
            if(definition is null||!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(key.Value),System.Text.Encoding.UTF8.GetBytes(definition.MachineKey)))throw new ManagementAccessDeniedException();
            return new(await diagnostics.ProbeAsync(origin,key,ct),new("available",await DemoCustomerClient.Info(definition,ct)));
        }
    }
}
