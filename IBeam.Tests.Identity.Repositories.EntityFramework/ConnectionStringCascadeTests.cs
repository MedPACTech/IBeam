using IBeam.Identity.Repositories.EntityFramework.Extensions;
using IBeam.Identity.Repositories.EntityFramework.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Identity.Repositories.EntityFramework;

[TestClass]
public sealed class ConnectionStringCascadeTests
{
    [TestMethod]
    public void AddIBeamIdentityEntityFrameworkStores_UsesScopedConfigSection_First()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IdentityEf:Provider"] = "Sqlite",
            ["IdentityEf:ConnectionString"] = "Data Source=scoped.db",
            ["IBeam:Identity:EntityFramework:ConnectionString"] = "Data Source=identity-ef.db",
            ["IBeam:Repositories:EntityFramework:ConnectionString"] = "Data Source=repo-ef.db",
            ["IBeam:Repositories:ConnectionString"] = "Data Source=repo.db",
            ["IBeam:ConnectionString"] = "Data Source=ibeam.db",
            ["ConnectionStrings:DefaultConnection"] = "Data Source=default.db"
        });

        var services = new ServiceCollection();
        services.AddIBeamIdentityEntityFrameworkStores(config);
        using var provider = services.BuildServiceProvider();

        var opts = provider.GetRequiredService<EntityFrameworkIdentityOptions>();
        Assert.AreEqual("Data Source=scoped.db", opts.ConnectionString);
    }

    [TestMethod]
    public void AddIBeamIdentityEntityFrameworkStores_FallsBackToRepositoriesConnectionString()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IdentityEf:Provider"] = "Sqlite",
            ["IBeam:Repositories:ConnectionString"] = "Data Source=repo.db",
            ["IBeam:ConnectionString"] = "Data Source=ibeam.db",
            ["ConnectionStrings:DefaultConnection"] = "Data Source=default.db"
        });

        var services = new ServiceCollection();
        services.AddIBeamIdentityEntityFrameworkStores(config);
        using var provider = services.BuildServiceProvider();

        var opts = provider.GetRequiredService<EntityFrameworkIdentityOptions>();
        Assert.AreEqual("Data Source=repo.db", opts.ConnectionString);
    }

    [TestMethod]
    public void AddIBeamIdentityEntityFrameworkStores_FallsBackToDefaultConnection()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["IdentityEf:Provider"] = "Sqlite",
            ["ConnectionStrings:DefaultConnection"] = "Data Source=default.db"
        });

        var services = new ServiceCollection();
        services.AddIBeamIdentityEntityFrameworkStores(config);
        using var provider = services.BuildServiceProvider();

        var opts = provider.GetRequiredService<EntityFrameworkIdentityOptions>();
        Assert.AreEqual("Data Source=default.db", opts.ConnectionString);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}

