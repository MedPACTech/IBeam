using IBeam.Identity.Api.DependencyInjection;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class CommunicationSenderRegistrationTests
{
    private static IConfiguration Configuration()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
        ["IBeam:Identity:AzureTable:StorageConnectionString"]="UseDevelopmentStorage=true",
        ["IBeam:Identity:Jwt:Issuer"]="sender-registration-test",["IBeam:Identity:Jwt:Audience"]="sender-registration-test",
        ["IBeam:Identity:Jwt:SigningKey"]=new string('K',64)
    }).Build();
    [TestMethod]
    public void ExistingSenderIsRequiredBeforeOptInComposition()
    {
        var error=Assert.ThrowsExactly<InvalidOperationException>(()=>new ServiceCollection().AddIBeamIdentityApiWithCommunicationSender(Configuration()));
        Assert.Contains("Register an IIdentityCommunicationSender",error.Message);
    }
    [TestMethod]
    public async Task ExistingSenderPreservesNormalJwtAndSessionValidationWithoutUnusedAzureProviders()
    {
        var services=new ServiceCollection();services.AddLogging();var sender=new Sender();services.AddSingleton<IIdentityCommunicationSender>(sender);
        services.AddIBeamIdentityApiWithCommunicationSender(Configuration());
        Assert.IsFalse(services.Any(x=>x.ServiceType.IsGenericType && x.ServiceType.GetGenericTypeDefinition()==typeof(IValidateOptions<>) &&
            x.ServiceType.GenericTypeArguments[0].Namespace?.StartsWith("IBeam.Communications",StringComparison.Ordinal)==true));
        await using var provider=services.BuildServiceProvider();Assert.AreSame(sender,provider.GetRequiredService<IIdentityCommunicationSender>());
        var jwt=provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.IsNotNull(jwt.Events.OnTokenValidated);Assert.IsNotNull(jwt.Events.OnAuthenticationFailed);
        Assert.IsTrue(jwt.TokenValidationParameters.ValidateIssuer);Assert.IsTrue(jwt.TokenValidationParameters.ValidateAudience);Assert.IsTrue(jwt.TokenValidationParameters.ValidateLifetime);
    }
    [TestMethod]
    public void ExistingApiRegistrationStillInstallsItsDefaultCommunicationAdapter()
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIBeamIdentityApi(Configuration());
        Assert.AreEqual("IdentityCommunicationAdapter",services.Last(x=>x.ServiceType==typeof(IIdentityCommunicationSender)).ImplementationType?.Name);
    }
    private sealed class Sender:IIdentityCommunicationSender
    {public Task SendAsync(IdentitySenderMessage message,CancellationToken ct=default)=>Task.CompletedTask;}
}
