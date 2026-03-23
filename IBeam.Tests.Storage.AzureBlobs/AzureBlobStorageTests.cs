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
}
