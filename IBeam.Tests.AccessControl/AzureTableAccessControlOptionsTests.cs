using IBeam.AccessControl.Repositories.AzureTable;

namespace IBeam.Tests.AccessControl;

[TestClass]
public sealed class AzureTableAccessControlOptionsTests
{
    [TestMethod]
    public void DefaultsCreateTablesIfNotExistsToTrue()
    {
        var options = new AzureTableAccessControlOptions
        {
            StorageConnectionString = "UseDevelopmentStorage=true"
        };

        options.Validate();

        Assert.IsTrue(options.CreateTablesIfNotExists);
        Assert.AreEqual("AccessGrants", options.ResourceAccessGrantsTableName);
        Assert.AreEqual("PermissionRoleMaps", options.PermissionRoleMapsTableName);
        Assert.AreEqual("ServiceOperationPermissions", options.ServiceOperationPermissionsTableName);
    }

    [TestMethod]
    public void AllowsCreateTablesIfNotExistsFalse()
    {
        var options = new AzureTableAccessControlOptions
        {
            StorageConnectionString = "UseDevelopmentStorage=true",
            CreateTablesIfNotExists = false
        };

        options.Validate();

        Assert.IsFalse(options.CreateTablesIfNotExists);
    }
}
