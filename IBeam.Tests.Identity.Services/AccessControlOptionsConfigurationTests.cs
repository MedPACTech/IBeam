using IBeam.Identity.Options;
using IBeam.Identity.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Services;

/// <summary>
/// Configuration must be able to NARROW the role and permission lists that decide who is an owner or an
/// administrator, not only widen them (IBM-0073).
/// </summary>
/// <remarks>
/// These bind through the real registration rather than constructing the options directly, because that
/// is where the defect lived: an object initializer replaces a list, while IConfiguration.Bind appends
/// to it. Tests that set these lists in code — as IBeam's own already did — pass either way, which is
/// exactly why nobody noticed that configuration could not remove a built-in role name.
/// </remarks>
[TestClass]
public sealed class AccessControlOptionsConfigurationTests
{
    [TestMethod]
    public void ConfiguredAdminRoleNames_ReplaceTheDefaults_RatherThanAddingToThem()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            [$"{IBeamAccessControlOptions.SectionName}:AdminRoleNames:0"] = "RegionalAdmin"
        });

        // Before the fix this was ["Administrator", "Admin", "RegionalAdmin"], so an operator who set
        // out to revoke administrator status from anyone named "Admin" silently did not.
        CollectionAssert.AreEqual(new[] { "RegionalAdmin" }, options.AdminRoleNames);
    }

    [TestMethod]
    public void ConfiguringOneList_LeavesTheOthersAtTheirDefaults()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            [$"{IBeamAccessControlOptions.SectionName}:AdminRoleNames:0"] = "RegionalAdmin"
        });

        CollectionAssert.AreEqual(new[] { "Owner" }, options.OwnerRoleNames);
        CollectionAssert.AreEqual(new[] { "Application" }, options.ApplicationRoleNames);
    }

    [TestMethod]
    public void ConfiguringNothing_KeepsEveryDefault()
    {
        var options = BuildOptions(new Dictionary<string, string?>());

        CollectionAssert.AreEqual(new[] { "Owner" }, options.OwnerRoleNames);
        CollectionAssert.AreEqual(new[] { "Administrator", "Admin" }, options.AdminRoleNames);
        CollectionAssert.AreEqual(
            new[] { "PlatformAdmin", "platform-admin", "Support" },
            options.AuthAttemptManagementRoleNames);
    }

    [TestMethod]
    public void EveryConfiguredList_ReplacesItsDefault()
    {
        // The defect was in the binder's treatment of collections, so it applied to all twelve lists on
        // this type, not only the two that happened to be noticed.
        var options = BuildOptions(new Dictionary<string, string?>
        {
            [$"{IBeamAccessControlOptions.SectionName}:OwnerRoleNames:0"] = "Proprietor",
            [$"{IBeamAccessControlOptions.SectionName}:AdminRoleNames:0"] = "RegionalAdmin",
            [$"{IBeamAccessControlOptions.SectionName}:ApplicationRoleNames:0"] = "Daemon",
            [$"{IBeamAccessControlOptions.SectionName}:TenantManagementPermissionNames:0"] = "tenants.manage",
            [$"{IBeamAccessControlOptions.SectionName}:AuthAttemptManagementRoleNames:0"] = "SecurityTeam",
            [$"{IBeamAccessControlOptions.SectionName}:AuthAttemptManagementPermissionNames:0"] = "unlock"
        });

        CollectionAssert.AreEqual(new[] { "Proprietor" }, options.OwnerRoleNames);
        CollectionAssert.AreEqual(new[] { "RegionalAdmin" }, options.AdminRoleNames);
        CollectionAssert.AreEqual(new[] { "Daemon" }, options.ApplicationRoleNames);
        CollectionAssert.AreEqual(new[] { "tenants.manage" }, options.TenantManagementPermissionNames);
        CollectionAssert.AreEqual(new[] { "SecurityTeam" }, options.AuthAttemptManagementRoleNames);
        CollectionAssert.AreEqual(new[] { "unlock" }, options.AuthAttemptManagementPermissionNames);
    }

    [TestMethod]
    public void ConfiguringSeveralValues_KeepsAllOfThem_AndOnlyThem()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            [$"{IBeamAccessControlOptions.SectionName}:AdminRoleNames:0"] = "RegionalAdmin",
            [$"{IBeamAccessControlOptions.SectionName}:AdminRoleNames:1"] = "GlobalAdmin"
        });

        CollectionAssert.AreEqual(new[] { "RegionalAdmin", "GlobalAdmin" }, options.AdminRoleNames);
    }

    private static IBeamAccessControlOptions BuildOptions(IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddIBeamIdentityServices(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<IBeamAccessControlOptions>>().Value;
    }
}
