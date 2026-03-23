namespace IBeam.Tests.Identity.Repositories.AzureTable;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void AzureTableExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Identity.Repositories.AzureTable.Extensions.ServiceCollectionExtensions));
    }
}
