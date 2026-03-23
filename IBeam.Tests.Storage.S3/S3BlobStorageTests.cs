using IBeam.Storage.Abstractions;
using IBeam.Storage.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Storage.S3;

[TestClass]
public sealed class S3BlobStorageTests
{
    [TestMethod]
    public void ExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Storage.S3.ServiceCollectionExtensions));
    }

    [TestMethod]
    public void Options_Validate_Throws_WhenMissingRegionAndServiceUrl()
    {
        var options = new S3BlobStorageOptions();
        Assert.ThrowsExactly<InvalidOperationException>(options.Validate);
    }

    [TestMethod]
    public void Options_Validate_Throws_WhenCredentialsPartial()
    {
        var options = new S3BlobStorageOptions
        {
            Region = "us-east-1",
            AccessKeyId = "key"
        };

        Assert.ThrowsExactly<InvalidOperationException>(options.Validate);
    }

    [TestMethod]
    public void GetBlobUrl_UsesPathStyleForCustomService_WhenEnabled()
    {
        var options = Options.Create(new S3BlobStorageOptions
        {
            ServiceUrl = "http://localhost:4566",
            ForcePathStyle = true,
            AccessKeyId = "test",
            SecretAccessKey = "test"
        });

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<S3BlobStorageService>.Instance;
        var service = new S3BlobStorageService(options, logger);

        var url = service.GetBlobUrl("bucket", "path/file.txt");
        Assert.AreEqual("http://localhost:4566/bucket/path%2Ffile.txt", url);
    }

    [TestMethod]
    public void GetBlobUrl_UsesAwsFormat_WhenRegionConfigured()
    {
        var options = Options.Create(new S3BlobStorageOptions
        {
            Region = "us-east-2"
        });

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<S3BlobStorageService>.Instance;
        var service = new S3BlobStorageService(options, logger);

        var url = service.GetBlobUrl("bucket", "file.txt");
        Assert.AreEqual("https://bucket.s3.us-east-2.amazonaws.com/file.txt", url);
    }

    [TestMethod]
    public void AddIBeamS3BlobStorage_RegistersBlobStorageService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{S3BlobStorageOptions.SectionName}:ServiceUrl"] = "http://localhost:4566",
                [$"{S3BlobStorageOptions.SectionName}:AccessKeyId"] = "test",
                [$"{S3BlobStorageOptions.SectionName}:SecretAccessKey"] = "test"
            })
            .Build();

        services.AddIBeamS3BlobStorage(config);

        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IBlobStorageService>();

        Assert.IsInstanceOfType<S3BlobStorageService>(storage);
    }
}
