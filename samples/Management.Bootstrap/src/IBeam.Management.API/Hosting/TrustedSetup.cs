using Azure.Data.Tables;
using IBeam.Identity.Schema;
using System.Security.Claims;
using IBeam.Management.Identity.Services;
using IBeam.Management.Services.Audit;

namespace IBeam.Management.API.Hosting;

public static class TrustedSetup
{
    // A terminating operator command. It never builds or starts a web server.
    public static async Task RunAsync(IConfiguration configuration, IHostEnvironment environment, CancellationToken ct = default)
    {
        var email = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_SETUP_EMAIL");
        var password = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_SETUP_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Supply the initial administrator through protected setup environment variables.");
        var setup = new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IBeam:Management:Identity:BootstrapEnabled"] = "true",
            ["IBeam:Management:Identity:Storage:ProvisionSchema"] = "true"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(setup);
        services.AddSingleton(environment);
        services.AddHttpContextAccessor();
        services.AddSingleton<IManagementPrincipalProvider, SetupAuditPrincipalProvider>();
        HostRegistration.AddDelivery(services, setup, environment);
        services.AddIBeamManagementIdentityServices(setup);
        HostRegistration.AddStores(services, setup);
        services.AddIBeamManagementAudit(setup);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IIdentitySchemaManager>().ApplyAsync(ct);
        foreach (var name in HostRegistration.TableNames(setup))
            await new TableClient(HostRegistration.StorageConnection(setup), name).CreateIfNotExistsAsync(ct);
        var result = await scope.ServiceProvider.GetRequiredService<IManagementIdentityBootstrapService>().BootstrapAsync(new()
        {
            OrganizationName = setup["ManagementHost:OrganizationName"] ?? "Support",
            AdministratorEmail = email,
            InitialPassword = password
        }, ct);
        Console.WriteLine($"Management schema ready. Organization: {result.OrganizationId}; administrator: {result.AdministratorUserId}. Runtime provisioning remains disabled.");
    }
    // Trusted CLI audit context only; it authenticates no HTTP caller and grants no permissions.
    private sealed class SetupAuditPrincipalProvider : IManagementPrincipalProvider
    {
        public ClaimsPrincipal GetPrincipal() => new();
    }
}
