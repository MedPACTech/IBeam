namespace IBeam.Tests.Communications.Email.Core;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void EmailTemplateOptions_SectionName_IsExpected()
    {
        Assert.AreEqual("IBeam:EmailTemplating", IBeam.Communications.Abstractions.EmailTemplateOptions.SectionName);
    }
}
