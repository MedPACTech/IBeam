using IBeam.Identity.Options;
using IBeam.Identity.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OtpOptionsConfigurationTests
{
    [TestMethod]
    public void AllowAutoProvision_DefaultsToTrue_InDevelopment_WhenNotConfigured()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Development"
        });

        Assert.IsTrue(options.AllowAutoProvisionForUnknownUser);
    }

    [TestMethod]
    public void AllowAutoProvision_DefaultsToFalse_InTestAndProduction_WhenNotConfigured()
    {
        var testOptions = BuildOptions(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Test"
        });
        var prodOptions = BuildOptions(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Production"
        });

        Assert.IsFalse(testOptions.AllowAutoProvisionForUnknownUser);
        Assert.IsFalse(prodOptions.AllowAutoProvisionForUnknownUser);
    }

    [TestMethod]
    public void AllowAutoProvision_ConfigOverride_Wins()
    {
        var options = BuildOptions(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["IBeam:Identity:Otp:AllowAutoProvisionForUnknownUser"] = "true"
        });

        Assert.IsTrue(options.AllowAutoProvisionForUnknownUser);
    }

    private static OtpOptions BuildOptions(IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddIBeamIdentityServices(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<OtpOptions>>().Value;
    }
}
