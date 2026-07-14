using IBeam.Identity.Authorization;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Authorization;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class PermissionCatalogProviderTests
{
    private static readonly Guid PermissionId = Guid.Parse("9f88f869-6ce7-4f30-aeb2-1a4fc4f7bf26");

    [TestMethod]
    public async Task GetExposedPermissionsAsync_IncludesAttributeConfigurationAndMappingSources()
    {
        var options = new PermissionAccessOptions
        {
            Catalog =
            [
                new PermissionCatalogEntry
                {
                    PermissionName = "Catalog.Config.Only",
                    Resource = "Configuration",
                    Description = "From configuration catalog."
                }
            ],
            Mappings =
            [
                new PermissionAccessMapEntry
                {
                    PermissionName = "Mapping.Config.Only",
                    RoleNames = ["admin"]
                }
            ]
        };

        var monitor = new Mock<IOptionsMonitor<PermissionAccessOptions>>(MockBehavior.Strict);
        monitor.SetupGet(x => x.CurrentValue).Returns(options);

        var sut = new PermissionCatalogProvider(monitor.Object);
        var all = await sut.GetExposedPermissionsAsync();

        Assert.IsTrue(all.Any(x => x.PermissionName == "Catalog.Config.Only" && x.Source == "configuration:catalog"));
        Assert.IsTrue(all.Any(x => x.PermissionName == "Mapping.Config.Only" && x.Source == "configuration:mapping"));
        Assert.IsTrue(all.Any(x => x.PermissionName == "PermissionCatalogProviderTests.Method.Save"));
        Assert.IsTrue(all.Any(x => x.PermissionName == "PermissionCatalogProviderTests.Operation.Delete" && x.Source == "attribute:operation"));
        Assert.IsTrue(all.Any(x => x.PermissionId == PermissionId));
    }

    [PermissionAccess("PermissionCatalogProviderTests.Class.Read")]
    [PermissionAccessId("9f88f869-6ce7-4f30-aeb2-1a4fc4f7bf26")]
    private sealed class CatalogDecoratedType
    {
        [PermissionAccess("PermissionCatalogProviderTests.Method.Save")]
        public void Save() { }

        [IBeamOperation("PermissionCatalogProviderTests.Operation.Delete", Label = "Delete test operation", IsDangerous = true)]
        [IBeamResourceAccess("test", "testId", "manage")]
        public void Delete(Guid testId) { }
    }
}
