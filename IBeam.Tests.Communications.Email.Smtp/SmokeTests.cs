namespace IBeam.Tests.Communications.Email.Smtp;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void ExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Communications.Email.Smtp.ServiceCollectionExtensions));
    }
}
