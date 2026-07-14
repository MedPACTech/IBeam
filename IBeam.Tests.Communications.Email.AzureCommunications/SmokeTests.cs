using IBeam.Communications.Email.AzureCommunications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Communications.Email.AzureCommunications;

[TestClass]
public sealed class SmokeTests
{
    private const string ConnectionStringKey = AzureCommunicationsEmailOptions.ConnectionStringConfigurationKey;

    [TestMethod]
    public void ExtensionType_IsAvailable()
    {
        Assert.IsNotNull(typeof(ServiceCollectionExtensions));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("<AZURE_COMMUNICATIONS_CONNECTION_STRING>")]
    [DataRow("DefaultEndpointsProtocol=https;AccountName=ibeam;AccountKey=fake;TableEndpoint=https://ibeam.table.core.windows.net/")]
    [DataRow("Server=tcp:ibeam.database.windows.net;Database=IBeam;User ID=app;Password=fake")]
    [DataRow("endpoint=https://ibeam.table.core.windows.net/;accesskey=fake")]
    [DataRow("endpoint=https://ibeam.communication.azure.com/")]
    public void OptionsValidation_RejectsMissingOrNonAcsConnectionString(string? connectionString)
    {
        var ex = Assert.ThrowsExactly<OptionsValidationException>(() => BuildOptions(connectionString));

        StringAssert.Contains(ex.Message, ConnectionStringKey);
        StringAssert.Contains(ex.Message, "Azure Communication Services connection string");
    }

    [TestMethod]
    public void OptionsValidation_AcceptsAcsConnectionString()
    {
        var options = BuildOptions("endpoint=https://ibeam.communication.azure.com/;accesskey=fakeKey");

        Assert.AreEqual("endpoint=https://ibeam.communication.azure.com/;accesskey=fakeKey", options.ConnectionString);
    }

    private static AzureCommunicationsEmailOptions BuildOptions(string? connectionString)
    {
        var values = connectionString is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { [ConnectionStringKey] = connectionString };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddIBeamAzureCommunicationsEmail(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<AzureCommunicationsEmailOptions>>().Value;
    }
}
