using IBeam.Storage.Abstractions;
using IBeam.Storage.AzureBlobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Storage.AzureBlobs;

[TestClass]
public sealed class AzureBlobStorageTests
{
    [TestMethod]
    public void ExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Storage.AzureBlobs.ServiceCollectionExtensions));
    }

    [TestMethod]
    public void Options_Validate_Throws_WhenMissingConfiguration()
    {
        var options = new AzureBlobStorageOptions();

        Assert.ThrowsExactly<InvalidOperationException>(options.Validate);
    }

    [TestMethod]
    public void Options_Validate_AllowsConnectionString()
    {
        var options = new AzureBlobStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true"
        };

        options.Validate();
    }

    [TestMethod]
    public void AddIBeamAzureBlobStorage_RegistersBlobStorageService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AzureBlobStorageOptions.SectionName}:ConnectionString"] = "UseDevelopmentStorage=true"
            })
            .Build();

        services.AddIBeamAzureBlobStorage(config);

        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IBlobStorageService>();

        Assert.IsInstanceOfType<AzureBlobStorageService>(storage);
    }

    [TestMethod]
    public void AddIBeamAzureBlobStorage_UsesScopedConnectionString_First()
    {
        var scoped = "UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://127.0.0.1/scoped";
        var @default = "UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://127.0.0.1/default";

        var options = BuildOptions(new Dictionary<string, string?>
        {
            [$"{AzureBlobStorageOptions.SectionName}:ConnectionString"] = scoped,
            ["ConnectionStrings:DefaultConnection"] = @default
        });

        Assert.AreEqual(scoped, options.ConnectionString);
    }

    [TestMethod]
    public void AddIBeamAzureBlobStorage_FallsBackToDefaultConnection()
    {
        var @default = "UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://127.0.0.1/default";

        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = @default
        });

        Assert.AreEqual(@default, options.ConnectionString);
    }

    [TestMethod]
    public void AddIBeamAzureBlobStorage_ServiceUriOverridesDefaultConnection()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            [$"{AzureBlobStorageOptions.SectionName}:ServiceUri"] = "https://ibeam.blob.core.windows.net",
            ["ConnectionStrings:DefaultConnection"] = "UseDevelopmentStorage=true"
        });

        Assert.IsNull(options.ConnectionString);
        Assert.AreEqual("https://ibeam.blob.core.windows.net", options.ServiceUri);
    }

    private static AzureBlobStorageOptions BuildOptions(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIBeamAzureBlobStorage(config);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureBlobStorageOptions>>().Value;
    }
}
