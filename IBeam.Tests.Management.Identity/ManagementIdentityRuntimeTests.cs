using System.Security.Cryptography;
using IBeam.Identity.Interfaces;
using IBeam.Management.Identity.Api;
using IBeam.Management.Identity.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace IBeam.Tests.Management.Identity;

[TestClass]
public sealed class ManagementIdentityRuntimeTests
{
    [TestMethod]
    [DataRow("BootstrapEnabled", "true")]
    [DataRow("Storage:ProvisionSchema", "true")]
    [DataRow("Jwt:AccessTokenMinutes", "60")]
    [DataRow("Jwt:SessionInactivityMinutes", "120")]
    [DataRow("Jwt:SessionAbsoluteLifetimeDays", "30")]
    [DataRow("Otp:VerificationTokenSecret", "")]
    [DataRow("Otp:HashSalt", "change-me")]
    [DataRow("Otp:MaxAttempts", "0")]
    [DataRow("LoginAttempts:Enabled", "false")]
    public async Task UnsafeRuntimeConfigurationFailsStartup(string key, string value)
    {
        var configuration = IdentityStorageTests.Configuration();
        configuration["IBeam:Management:Identity:BootstrapEnabled"] = "false";
        configuration["IBeam:Management:Identity:Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        configuration["IBeam:Management:Identity:Otp:HashSalt"] = Guid.NewGuid().ToString("N");
        configuration["IBeam:Management:Identity:Otp:VerificationTokenSecret"] = Guid.NewGuid().ToString("N");
        configuration["IBeam:Management:Identity:" + key] = value;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Configuration.AddConfiguration(configuration);
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton(Mock.Of<IIdentityCommunicationSender>());
        builder.Services.AddControllers().ConfigureApplicationPartManager(p => p.ApplicationParts.Clear());
        builder.Services.AddIBeamManagementIdentityApi(builder.Configuration);
        await using var app = builder.Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
    }

    [TestMethod]
    public void ReusingCustomerAudienceIsRejectedBeforeRegistration()
    {
        var configuration = IdentityStorageTests.Configuration();
        configuration["IBeam:Management:Identity:Jwt:Audience"] = configuration["IBeam:Identity:Jwt:Audience"];
        Assert.Throws<InvalidOperationException>(() => ManagementIdentityConfiguration.Build(configuration));
    }
}
