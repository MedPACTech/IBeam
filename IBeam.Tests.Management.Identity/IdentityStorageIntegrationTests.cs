using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Schema;
using IBeam.Management.Identity.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Management.Identity;

[TestClass]
public sealed class IdentityStorageIntegrationTests
{
    [TestMethod]
    [TestCategory("StorageIntegration")]
    [DataRow("Separate")]
    [DataRow("Shared")]
    public async Task RealProviderKeepsSameEmailRolesOtpAndSessionsIsolated(string mode)
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Set IBEAM_MANAGEMENT_TEST_STORAGE to an isolated Azurite fixture.");
        var prefix = "T" + Guid.NewGuid().ToString("N")[..12];
        var customerConfig = IdentityStorageTests.Configuration("Separate", prefix + "Customer");
        customerConfig["IBeam:Management:Identity:Storage:ConnectionString"] = connection;
        var managementConfig = IdentityStorageTests.Configuration(mode, prefix + "Management");
        var managementConnection = mode == "Separate" ? Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_SEPARATE_STORAGE") : connection;
        if (string.IsNullOrWhiteSpace(managementConnection)) Assert.Inconclusive("Separate mode requires a second isolated fixture endpoint.");
        managementConfig["IBeam:Management:Identity:Storage:ConnectionString"] = managementConnection;
        // Separate mode compares against no customer endpoint: each host owns only its own realm.
        if (mode == "Shared")
        {
            managementConfig["IBeam:Identity:AzureTable:StorageConnectionString"] = connection;
            managementConfig["IBeam:Identity:AzureTable:TablePrefix"] = prefix + "Customer";
        }
        customerConfig["IBeam:Management:Identity:Storage:ProvisionSchema"] = "true";
        managementConfig["IBeam:Management:Identity:Storage:ProvisionSchema"] = "true";
        await using var customer = Build(customerConfig);
        await using var management = Build(managementConfig);
        await using var customerScope = customer.CreateAsyncScope();
        await using var managementScope = management.CreateAsyncScope();
        await customerScope.ServiceProvider.GetRequiredService<IIdentitySchemaManager>().ApplyAsync();
        await managementScope.ServiceProvider.GetRequiredService<IIdentitySchemaManager>().ApplyAsync();
        var userStore = customerScope.ServiceProvider.GetRequiredService<IIdentityUserStore>();
        var created = await userStore.CreateAsync(new RegisterUserRequest("same@example.test", null, "Fixture-Only-Password!91"));
        Assert.IsTrue(created.Succeeded);
        var supportStore = managementScope.ServiceProvider.GetRequiredService<IIdentityUserStore>();
        Assert.IsNull(await supportStore.FindByEmailAsync("same@example.test"));
        var support = await supportStore.CreateAsync(new RegisterUserRequest("same@example.test", null, "Different-Fixture-Only!92"));
        Assert.IsTrue(support.Succeeded);
        Assert.IsFalse(await supportStore.ValidatePasswordAsync("same@example.test", "Fixture-Only-Password!91"));
        Assert.IsFalse(await userStore.ValidatePasswordAsync("same@example.test", "Different-Fixture-Only!92"));
        var setup = managementScope.ServiceProvider.GetRequiredService<IManagementIdentityBootstrapService>();
        var request = new ManagementBootstrapRequest { OrganizationName = "Support", AdministratorEmail = "bootstrap@example.test", InitialPassword = "Fixture-Bootstrap-Only!93" };
        var first = await setup.BootstrapAsync(request);
        var second = await setup.BootstrapAsync(request);
        Assert.IsTrue(first.CreatedUser);
        Assert.IsFalse(second.CreatedUser);
        Assert.AreEqual(first.AdministratorUserId, second.AdministratorUserId);
        var userId = created.User!.UserId;
        var memberships = managementScope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
        Assert.HasCount(0, await memberships.GetTenantsForUserAsync(userId));
        var sessions = customerScope.ServiceProvider.GetRequiredService<IAuthSessionStore>();
        await sessions.SaveAsync(new AuthSessionRecord(Guid.NewGuid().ToString("N"), "customer-session", userId,
            Guid.NewGuid(), "[]", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
        Assert.HasCount(0, await managementScope.ServiceProvider.GetRequiredService<IAuthSessionStore>().GetByUserAsync(userId));
        var client = new TableServiceClient(connection);
        var managementClient = new TableServiceClient(managementConnection);
        var customerOtp = client.GetTableClient(prefix + "CustomerOtpChallenges");
        await customerOtp.UpsertEntityAsync(new TableEntity("fixture", "otp") { ["Marker"] = "customer-only" });
        var supportOtp = managementClient.GetTableClient(prefix + "ManagementOtpChallenges");
        Assert.IsFalse((await supportOtp.GetEntityIfExistsAsync<TableEntity>("fixture", "otp")).HasValue);
        Assert.IsTrue((await customerOtp.GetEntityIfExistsAsync<TableEntity>("fixture", "otp")).HasValue);
        Assert.IsTrue((await client.GetTableClient(prefix + "CustomerSchema").GetEntityIfExistsAsync<TableEntity>("Schema", "IBeam.Identity")).HasValue);
        Assert.IsTrue((await managementClient.GetTableClient(prefix + "ManagementSchema").GetEntityIfExistsAsync<TableEntity>("Schema", "IBeam.Identity")).HasValue);
        Assert.HasCount(0, await customerScope.ServiceProvider.GetRequiredService<ITenantRoleStore>().GetRolesAsync(first.OrganizationId));
        Assert.IsTrue(await userStore.ValidatePasswordAsync("same@example.test", "Fixture-Only-Password!91"));
        // Cleanup only this test's random prefix in the explicitly isolated fixture.
        await foreach (var table in client.QueryAsync())
            if (table.Name.StartsWith(prefix, StringComparison.Ordinal)) await client.DeleteTableAsync(table.Name);
        if (mode == "Separate")
            await foreach (var table in managementClient.QueryAsync())
                if (table.Name.StartsWith(prefix, StringComparison.Ordinal)) await managementClient.DeleteTableAsync(table.Name);
    }

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddDataProtection();
        services.AddIBeamManagementIdentityServices(configuration);
        return services.BuildServiceProvider();
    }
}
