namespace IBeam.Tests.Communications.Email.PickupDirectory;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void ExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(global::IBeam.Communications.Email.PickupDirectory.ServiceCollectionExtensions));
    }
}
