namespace IBeam.Tests.Identity.Repositories.EntityFramework;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void EntityFrameworkExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Identity.Repositories.EntityFramework.Extensions.EntityFrameworkIdentityServiceCollectionExtensions));
    }
}
