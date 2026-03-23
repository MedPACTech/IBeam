namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void ApiExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Identity.Api.DependencyInjection.ServiceCollectionExtensions));
    }
}
