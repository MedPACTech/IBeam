namespace IBeam.Tests.Communications;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void EmailOptions_SectionName_IsExpected()
    {
        Assert.AreEqual("IBeam:Communications:Email", IBeam.Communications.Abstractions.EmailOptions.SectionName);
    }
}
