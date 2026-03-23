namespace IBeam.Tests.Identity;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void IdentityOptions_SectionName_IsExpected()
    {
        Assert.AreEqual("IBeam:Identity", global::IBeam.Identity.Options.IdentityOptions.SectionName);
    }
}
