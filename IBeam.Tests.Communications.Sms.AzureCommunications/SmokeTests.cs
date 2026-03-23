namespace IBeam.Tests.Communications.Sms.AzureCommunications;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void ExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Communications.Sms.AzureCommunications.ServiceCollectionExtensions));
    }
}
