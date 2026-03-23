namespace IBeam.Tests.Repositories;

[TestClass]
public sealed class SmokeTests
{
    [TestMethod]
    public void RepositoryOptions_SoftDeleteEnabledByDefault()
    {
        var options = new global::IBeam.Repositories.Core.RepositoryOptions();
        Assert.IsFalse(options.DisableSoftDelete);
    }
}
