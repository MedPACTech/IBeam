namespace IBeam.Tests.Utilities;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void TokenGeneratorType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Utilities.TokenGenerator));
    }
}
