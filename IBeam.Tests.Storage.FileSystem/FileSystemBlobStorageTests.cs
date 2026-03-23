using IBeam.Storage.Abstractions;
using IBeam.Storage.FileSystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Storage.FileSystem;

[TestClass]
public sealed class FileSystemBlobStorageTests
{
    private string _rootPath = string.Empty;
    private FileSystemBlobStorageService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "IBeam.Tests.Storage.FileSystem", Guid.NewGuid().ToString("N"));

        var options = Microsoft.Extensions.Options.Options.Create(new FileSystemBlobStorageOptions
        {
            RootPath = _rootPath
        });

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<FileSystemBlobStorageService>.Instance;
        _service = new FileSystemBlobStorageService(options, logger);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, true);
        }
    }

    [TestMethod]
    public async Task SaveGetExistsOpenReadDelete_RoundTrip_Works()
    {
        await using var writeStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello"));
        await _service.SaveAsync("docs", "sample.txt", writeStream);

        var exists = await _service.ExistsAsync("docs", "sample.txt");
        var data = await _service.GetAsync("docs", "sample.txt");

        Assert.IsTrue(exists);
        CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("hello"), data);

        await using (var readStream = await _service.OpenReadAsync("docs", "sample.txt"))
        {
            Assert.IsNotNull(readStream);

            using var sr = new StreamReader(readStream!);
            var text = await sr.ReadToEndAsync();
            Assert.AreEqual("hello", text);
        }

        await _service.DeleteAsync("docs", "sample.txt");
        Assert.IsFalse(await _service.ExistsAsync("docs", "sample.txt"));
    }

    [TestMethod]
    public async Task OpenReadAsync_ReturnsNull_WhenMissing()
    {
        var stream = await _service.OpenReadAsync("docs", "missing.txt");
        Assert.IsNull(stream);
    }

    [TestMethod]
    public async Task SaveAsync_Throws_OnPathTraversal()
    {
        await using var writeStream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsExactlyAsync<BlobStorageException>(async () =>
            await _service.SaveAsync("docs", "..\\evil.txt", writeStream));
    }

    [TestMethod]
    public void AddIBeamFileSystemBlobStorage_RegistersBlobStorageService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{FileSystemBlobStorageOptions.SectionName}:RootPath"] = _rootPath
            })
            .Build();

        services.AddIBeamFileSystemBlobStorage(config);

        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IBlobStorageService>();

        Assert.IsInstanceOfType<FileSystemBlobStorageService>(storage);
    }
}
