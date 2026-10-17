using IBeam.Management.Models;
using IBeam.Management.Repositories.AzureTable.Diagnostics;
using IBeam.Management.Services.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Diagnostics;

[TestClass]
public sealed class DiagnosticConfigurationTests
{
    [TestMethod]
    [DataRow("source")]
    [DataRow("confirmation")]
    [DataRow("shared-confirmation")]
    [DataRow("key")]
    [DataRow("window")]
    [DataRow("page")]
    [DataRow("scan")]
    [DataRow("requests")]
    [DataRow("timeout")]
    public void UnsafeOrAmbiguousReaderSettingsAreRejected(string change)
    {
        var f = new ErrorReaderFixture();
        switch(change)
        {
            case "source": f.Options.SourceMode = DiagnosticSourceMode.Unspecified; break;
            case "confirmation": f.Options.DedicatedSourceConfirmed = false; break;
            case "shared-confirmation": f.Options.SourceMode = DiagnosticSourceMode.TaggedShared; break;
            case "key": f.Options.ProtectionKey = "not-a-secret-example"; break;
            case "window": f.Options.MaximumWindowHours = 1000; break;
            case "page": f.Options.MaximumPageSize = 1001; break;
            case "scan": f.Options.ScanBudgetPerPage = 0; break;
            case "requests": f.Options.MaximumStorageRequestsPerPage = 0; break;
            case "timeout": f.Options.RequestTimeoutSeconds = 0; break;
        }
        Assert.IsFalse(f.Options.IsValid());
        Assert.Throws<InvalidOperationException>(() => f.Reader());
    }

    [TestMethod]
    public void DiagnosticsNeverInheritPortalIdentityConnectionOrTablePrefix()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IBeam:Identity:AzureTable:StorageConnectionString"] = "customer-config-only",
            ["IBeam:Management:Identity:Storage:ConnectionString"] = "portal-config-only",
            ["IBeam:Management:Identity:Storage:TablePrefix"] = "Management"
        }).Build();
        var services = new ServiceCollection();
        services.AddIBeamManagementAzureTableSystemErrorReader(config);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ISystemErrorReadRepository>());
    }

    [TestMethod]
    public void ReaderRegistrationRejectsDefaultUnspecifiedSource()
    {
        var services = new ServiceCollection();
        services.AddIBeamManagementSystemErrorReader(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SystemErrorReaderOptions>>().Value);
    }
}
