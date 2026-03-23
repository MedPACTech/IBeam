namespace IBeam.Tests.Communications.Email.SendGrid;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void ExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Communications.Email.SendGrid.ServiceCollectionExtensions));
    }
}
