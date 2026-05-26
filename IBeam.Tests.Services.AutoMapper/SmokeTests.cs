namespace IBeam.Tests.Services.AutoMapper;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void AutoMapperExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Services.AutoMapper.ServiceCollectionExtensions));
    }
}
