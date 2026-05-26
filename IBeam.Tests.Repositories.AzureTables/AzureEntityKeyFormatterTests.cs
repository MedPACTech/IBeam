using IBeam.Repositories.Abstractions;
using IBeam.Repositories.AzureTables;

namespace IBeam.Tests.Repositories.AzureTables;

[TestClass]
public sealed class AzureEntityKeyFormatterTests
{
    [TestMethod]
    public void Formatter_WithNFormat_FormatsAndReturnsLookupCandidates()
    {
        var id = Guid.Parse("9a7a671f-a4f0-4f7e-9195-f74f61ad2d69");
        var formatter = new AzureEntityKeyFormatter("N", enableLegacyFallbackReads: false);

        var value = formatter.Format(id);
        var lookup = formatter.GetLookupCandidates(id);

        Assert.AreEqual("9a7a671fa4f04f7e9195f74f61ad2d69", value);
        Assert.AreEqual(1, lookup.Count);
        Assert.AreEqual(value, lookup[0]);
    }

    [TestMethod]
    public void Formatter_WithDFormatAndLegacyFallback_ReturnsPrimaryThenLegacy()
    {
        var id = Guid.Parse("9a7a671f-a4f0-4f7e-9195-f74f61ad2d69");
        var formatter = new AzureEntityKeyFormatter("D", enableLegacyFallbackReads: true);

        var lookup = formatter.GetLookupCandidates(id);

        Assert.AreEqual(2, lookup.Count);
        Assert.AreEqual("9a7a671f-a4f0-4f7e-9195-f74f61ad2d69", lookup[0]);
        Assert.AreEqual("9a7a671fa4f04f7e9195f74f61ad2d69", lookup[1]);
    }

    [TestMethod]
    public void Resolver_UsesFormatterForDefaultWriteKey()
    {
        var formatter = new AzureEntityKeyFormatter("D", enableLegacyFallbackReads: false);
        var strategy = AzureTablePartitionKeyStrategies.Global<TestEntity>("global");
        var resolver = new AzureEntityKeyResolver<TestEntity>(strategy, mapping: null, keyFormatter: formatter);

        var entity = new TestEntity { Id = Guid.Parse("9a7a671f-a4f0-4f7e-9195-f74f61ad2d69") };
        var key = resolver.ResolveWriteKey(null, entity);

        Assert.AreEqual("global", key.PartitionKey);
        Assert.AreEqual("9a7a671f-a4f0-4f7e-9195-f74f61ad2d69", key.RowKey);
    }

    [TestMethod]
    public void Resolver_UsesMappingWriteKeyWhenProvided()
    {
        var formatter = new AzureEntityKeyFormatter("N", enableLegacyFallbackReads: false);
        var strategy = AzureTablePartitionKeyStrategies.Global<TestEntity>("global");
        var mapping = new AzureEntityMappingOptions<TestEntity>
        {
            TableName = "TestEntity",
            WriteKey = (_, entity) => new AzureEntityKey
            {
                PartitionKey = "mapped",
                RowKey = $"rk-{entity.Id:D}"
            }
        };

        var resolver = new AzureEntityKeyResolver<TestEntity>(strategy, mapping, formatter);
        var entity = new TestEntity { Id = Guid.Parse("9a7a671f-a4f0-4f7e-9195-f74f61ad2d69") };

        var key = resolver.ResolveWriteKey(null, entity);
        Assert.AreEqual("mapped", key.PartitionKey);
        Assert.AreEqual("rk-9a7a671f-a4f0-4f7e-9195-f74f61ad2d69", key.RowKey);
    }

    private sealed class TestEntity : IEntity
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
    }
}
