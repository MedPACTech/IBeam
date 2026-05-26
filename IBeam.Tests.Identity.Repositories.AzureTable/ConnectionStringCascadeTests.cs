using IBeam.Identity.Repositories.AzureTable.Extensions;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Repositories.AzureTable;

[TestClass]
public sealed class ConnectionStringCascadeTests
{
    [TestMethod]
    public void AddIBeamIdentityAzureTable_UsesScopedStorageConnectionString_First()
    {
        var scoped = BuildAzureConnectionString("scopedacct");
        var topAzureTables = BuildAzureConnectionString("topacct");
        var repo = BuildAzureConnectionString("repoacct");
        var ibeam = BuildAzureConnectionString("ibeamacct");
        var @default = BuildAzureConnectionString("defaultacct");

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IBeam:Identity:AzureTable:StorageConnectionString"] = scoped,
            ["IBeam:AzureTables"] = topAzureTables,
            ["IBeam:Repositories:ConnectionString"] = repo,
            ["IBeam:ConnectionString"] = ibeam,
            ["ConnectionStrings:DefaultConnection"] = @default
        });

        var services = new ServiceCollection();
        services.AddIBeamIdentityAzureTable(config);
        using var provider = services.BuildServiceProvider();

        var opts = provider.GetRequiredService<IOptions<AzureTableIdentityOptions>>().Value;
        Assert.AreEqual(scoped, opts.StorageConnectionString);
    }

    [TestMethod]
    public void AddIBeamIdentityAzureTable_FallsBackToIBeamRepositoriesConnectionString()
    {
        var repo = BuildAzureConnectionString("repoacct");
        var ibeam = BuildAzureConnectionString("ibeamacct");
        var @default = BuildAzureConnectionString("defaultacct");

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IBeam:Repositories:ConnectionString"] = repo,
            ["IBeam:ConnectionString"] = ibeam,
            ["ConnectionStrings:DefaultConnection"] = @default
        });

        var services = new ServiceCollection();
        services.AddIBeamIdentityAzureTable(config);
        using var provider = services.BuildServiceProvider();

        var opts = provider.GetRequiredService<IOptions<AzureTableIdentityOptions>>().Value;
        Assert.AreEqual(repo, opts.StorageConnectionString);
    }

    [TestMethod]
    public void AddIBeamIdentityAzureTable_FallsBackToDefaultConnection_WhenOthersMissing()
    {
        var @default = BuildAzureConnectionString("defaultacct");

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = @default
        });

        var services = new ServiceCollection();
        services.AddIBeamIdentityAzureTable(config);
        using var provider = services.BuildServiceProvider();

        var opts = provider.GetRequiredService<IOptions<AzureTableIdentityOptions>>().Value;
        Assert.AreEqual(@default, opts.StorageConnectionString);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static string BuildAzureConnectionString(string discriminator)
        => $"UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://127.0.0.1/{discriminator}";
}
