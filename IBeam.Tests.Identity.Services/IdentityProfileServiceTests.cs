using IBeam.Identity.Interfaces;
using IBeam.Identity.Services.Profiles;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class IdentityProfileServiceTests
{
    [TestMethod]
    public async Task UpsertAndGet_ReturnsMergedAttributes()
    {
        IIdentityProfileStore store = new InMemoryIdentityProfileStore();
        var sut = new IdentityProfileService(store);
        var userId = Guid.NewGuid();

        await sut.UpsertAsync(userId, new Dictionary<string, string>
        {
            ["billing.customerId"] = "cus_123",
            ["billing.plan"] = "pro"
        });

        var result = await sut.GetAsync(userId);

        Assert.AreEqual(userId, result.UserId);
        Assert.AreEqual("cus_123", result.Attributes["billing.customerId"]);
        Assert.AreEqual("pro", result.Attributes["billing.plan"]);
    }
}
