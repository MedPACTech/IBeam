namespace IBeam.Tests.Repositories.OrmLite;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void OrmLiteExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Repositories.OrmLite.ServiceCollectionExtensions));
    }
}
