using System.Text.Json;
using IBeam.AccessControl.Repositories.AzureTable;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Repositories.AzureTable.Extensions;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Schema;
using IBeam.Management.Identity.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Identity;

[TestClass]
public sealed class IdentityStorageTests
{
    internal static IConfigurationRoot Configuration(string mode = "Separate", string prefix = "Management")
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IBeam:Management:Identity:OrganizationId"] = Guid.NewGuid().ToString(),
            ["IBeam:Management:Identity:Storage:Mode"] = mode,
            ["IBeam:Management:Identity:Storage:ConnectionString"] = "UseDevelopmentStorage=true",
            ["IBeam:Management:Identity:Storage:TablePrefix"] = prefix,
            ["IBeam:Identity:AzureTable:StorageConnectionString"] = mode == "Shared" ? "UseDevelopmentStorage=true" : null,
            ["IBeam:Identity:AzureTable:TablePrefix"] = "Customer",
            ["IBeam:Identity:Jwt:Audience"] = "customer",
            ["IBeam:Management:Identity:Jwt:Audience"] = "management",
            ["IBeam:Management:Identity:BootstrapEnabled"] = "true"
        }).Build();

    [TestMethod]
    [DataRow("Separate")]
    [DataRow("Shared")]
    public void ManagementSnapshotMapsEntireIdentityAndAccessControlRealm(string mode)
    {
        var source = Configuration(mode);
        var isolated = ManagementIdentityConfiguration.Build(source);
        var identity = isolated.GetSection(AzureTableIdentityOptions.SectionName).Get<AzureTableIdentityOptions>()!;
        var access = isolated.GetSection(AzureTableAccessControlOptions.SectionName).Get<AzureTableAccessControlOptions>()!;
        Assert.AreEqual("Management", identity.TablePrefix);
        Assert.AreEqual("Management", access.TablePrefix);
        Assert.IsFalse(identity.CreateTablesIfNotExists);
        Assert.IsFalse(access.CreateTablesIfNotExists);
        Assert.AreEqual("management", isolated["IBeam:Identity:Jwt:Audience"]);
        Assert.AreEqual("customer", source["IBeam:Identity:Jwt:Audience"]);
        Assert.AreEqual("Customer", source[AzureTableIdentityOptions.SectionName + ":TablePrefix"]);
        Assert.AreEqual("false", isolated["IBeam:Identity:Otp:AllowAutoProvisionForUnknownUser"]);
        Assert.AreEqual("RequireExistingTenant", isolated["IBeam:Identity:TenantProvisioning:Mode"]);
        foreach (var property in typeof(AzureTableIdentityOptions).GetProperties().Where(p => p.Name.EndsWith("TableName")))
            Assert.StartsWith("Management", identity.FullTableName((string)property.GetValue(identity)!));
        Assert.AreEqual("ManagementSchema", identity.TablePrefix + "Schema");
    }

    [TestMethod]
    [DataRow("Customer")]
    [DataRow("customer")]
    [DataRow("")]
    public void RejectsSharedTableNameCollisionsOrEmptyPrefix(string prefix)
        => Assert.Throws<InvalidOperationException>(() => ManagementIdentityConfiguration.Build(Configuration("Shared", prefix)));

    [TestMethod]
    public void RejectsCustomCustomerTableCollision()
    {
        var source = Configuration("Shared");
        source["IBeam:Identity:AzureTable:TablePrefix"] = "";
        source["IBeam:Identity:AzureTable:AuthSessionsTableName"] = "ManagementAuthSessions";
        Assert.Throws<InvalidOperationException>(() => ManagementIdentityConfiguration.Build(source));
    }

    [TestMethod]
    public void SeparateStorageDoesNotFallBackToCustomerConnection()
    {
        var source = Configuration();
        source["IBeam:Management:Identity:Storage:ConnectionString"] = "";
        source["IBeam:Identity:AzureTable:StorageConnectionString"] = "UseDevelopmentStorage=true";
        Assert.Throws<InvalidOperationException>(() => ManagementIdentityConfiguration.Build(source));
    }

    [TestMethod]
    public void SeparateModeCannotQuietlyShareCustomerEndpoint()
    {
        var source = Configuration();
        source["IBeam:Identity:AzureTable:StorageConnectionString"] = "UseDevelopmentStorage=true";
        Assert.Throws<InvalidOperationException>(() => ManagementIdentityConfiguration.Build(source));
    }

    [TestMethod]
    public void RuntimeDoesNotRegisterSchemaStartupAndCannotMixIdentityProviders()
    {
        var services = new ServiceCollection();
        services.AddIBeamManagementIdentityServices(Configuration());
        Assert.IsFalse(services.Any(d => d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType?.Name == "AzureTableIdentitySchemaHostedService"));
        Assert.Throws<InvalidOperationException>(() => services.AddIBeamManagementIdentityServices(Configuration()));
    }

    [TestMethod]
    public void ExistingProviderOverloadStillRegistersSchemaStartup()
    {
        var isolated = ManagementIdentityConfiguration.Build(Configuration());
        var services = new ServiceCollection();
        services.AddIBeamIdentityAzureTable(isolated);
        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType?.Name == "AzureTableIdentitySchemaHostedService"));
    }

    [TestMethod]
    public void SecretBearingSetupModelsDoNotExposeSecretsInSerializationOrToString()
    {
        var password = Guid.NewGuid().ToString();
        var request = new ManagementBootstrapRequest { InitialPassword = password };
        Assert.IsFalse(request.ToString().Contains(password));
        Assert.IsFalse(JsonSerializer.Serialize(request).Contains(password));
        Assert.IsFalse(new ManagementIdentityStorageOptions { ConnectionString = password }.ToString().Contains(password));
    }
}
