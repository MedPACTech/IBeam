using System.Net;
using System.Net.Http;
using System.Text;
using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Abstractions.Options;
using IBeam.Identity.Services.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OAuthAuthServiceTests
{
    [TestMethod]
    public async Task CompleteOAuthAsync_WithExistingUserSingleTenant_ReturnsToken()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        const string provider = "google";
        const string redirectUri = "https://localhost:5001/signin/oauth";

        var options = new OAuthOptions
        {
            StateTtlMinutes = 10,
            Providers = new Dictionary<string, OAuthProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                [provider] = new()
                {
                    Enabled = true,
                    ClientId = "client-id",
                    ClientSecret = "client-secret",
                    AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                    TokenEndpoint = "https://oauth2.googleapis.com/token",
                    UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo",
                    Scope = "openid profile email"
                }
            }
        };

        var handler = new QueueHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"oauth-access-token"}""", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"sub":"provider-user-1","email":"abram.cookson@outlook.com","name":"Abram","email_verified":true}""", Encoding.UTF8, "application/json")
            });

        var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        users.Setup(x => x.FindByEmailAsync("abram.cookson@outlook.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUser(userId, "abram.cookson@outlook.com", true));
        users.Setup(x => x.SetEmailConfirmedAsync(userId, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var externalLogins = new Mock<IExternalLoginStore>(MockBehavior.Strict);
        externalLogins.Setup(x => x.FindByProviderAsync(provider, "provider-user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalLoginInfo?)null);
        externalLogins.Setup(x => x.LinkAsync(userId, provider, "provider-user-1", "abram.cookson@outlook.com", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        tenants.Setup(x => x.GetTenantsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInfo> { new(tenantId, "Tenant A", new List<string> { "User" }, true) });

        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        tokens.Setup(x => x.CreateAccessTokenAsync(
                userId,
                tenantId,
                It.IsAny<IReadOnlyList<ClaimItem>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResult("jwt-token", DateTimeOffset.UtcNow.AddMinutes(30), new List<ClaimItem>()));

        var sut = new OAuthAuthService(
            new MemoryCache(new MemoryCacheOptions()),
            httpClientFactory.Object,
            OptionsMonitorOf(options),
            users.Object,
            externalLogins.Object,
            tenants.Object,
            Mock.Of<ITenantProvisioningService>(MockBehavior.Strict),
            tokens.Object);

        var start = await sut.StartOAuthAsync(provider, redirectUri);
        var result = await sut.CompleteOAuthAsync(new OAuthCallbackRequest(provider, start.State, "oauth-code", redirectUri));

        Assert.IsNotNull(result.Token);
        Assert.AreEqual("jwt-token", result.Token!.AccessToken);
        Assert.IsFalse(result.RequiresTenantSelection);
        Assert.IsFalse(result.IsNewUser);

        users.VerifyAll();
        externalLogins.VerifyAll();
        tenants.VerifyAll();
        tokens.VerifyAll();
    }

    [TestMethod]
    public async Task CompleteOAuthAsync_WhenStateIsInvalid_ThrowsValidation()
    {
        var options = new OAuthOptions
        {
            Providers = new Dictionary<string, OAuthProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["google"] = new()
                {
                    Enabled = true,
                    ClientId = "client-id",
                    ClientSecret = "client-secret",
                    AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                    TokenEndpoint = "https://oauth2.googleapis.com/token",
                    UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo"
                }
            }
        };

        var sut = new OAuthAuthService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<IHttpClientFactory>(MockBehavior.Strict),
            OptionsMonitorOf(options),
            Mock.Of<IIdentityUserStore>(MockBehavior.Strict),
            Mock.Of<IExternalLoginStore>(MockBehavior.Strict),
            Mock.Of<ITenantMembershipStore>(MockBehavior.Strict),
            Mock.Of<ITenantProvisioningService>(MockBehavior.Strict),
            Mock.Of<ITokenService>(MockBehavior.Strict));

        await AssertThrowsAsync<IdentityValidationException>(() =>
            sut.CompleteOAuthAsync(new OAuthCallbackRequest(
                "google",
                "missing-state",
                "oauth-code",
                "https://localhost:5001/signin/oauth")));
    }

    [TestMethod]
    public async Task StartOAuthAsync_WhenProviderDisabled_ThrowsValidation()
    {
        var options = new OAuthOptions
        {
            Providers = new Dictionary<string, OAuthProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["google"] = new()
                {
                    Enabled = false,
                    ClientId = "client-id",
                    ClientSecret = "client-secret",
                    AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                    TokenEndpoint = "https://oauth2.googleapis.com/token",
                    UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo"
                }
            }
        };

        var sut = new OAuthAuthService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<IHttpClientFactory>(MockBehavior.Strict),
            OptionsMonitorOf(options),
            Mock.Of<IIdentityUserStore>(MockBehavior.Strict),
            Mock.Of<IExternalLoginStore>(MockBehavior.Strict),
            Mock.Of<ITenantMembershipStore>(MockBehavior.Strict),
            Mock.Of<ITenantProvisioningService>(MockBehavior.Strict),
            Mock.Of<ITokenService>(MockBehavior.Strict));

        await AssertThrowsAsync<IdentityValidationException>(() =>
            sut.StartOAuthAsync("google", "https://localhost:5001/signin/oauth"));
    }

    [TestMethod]
    public async Task CompleteOAuthAsync_WhenEmailNotVerified_ThrowsValidation()
    {
        const string provider = "google";
        const string redirectUri = "https://localhost:5001/signin/oauth";

        var options = new OAuthOptions
        {
            StateTtlMinutes = 10,
            Providers = new Dictionary<string, OAuthProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                [provider] = new()
                {
                    Enabled = true,
                    ClientId = "client-id",
                    ClientSecret = "client-secret",
                    AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                    TokenEndpoint = "https://oauth2.googleapis.com/token",
                    UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo",
                    Scope = "openid profile email"
                }
            }
        };

        var handler = new QueueHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"oauth-access-token"}""", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"sub":"provider-user-1","email":"abram.cookson@outlook.com","name":"Abram","email_verified":false}""", Encoding.UTF8, "application/json")
            });

        var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var sut = new OAuthAuthService(
            new MemoryCache(new MemoryCacheOptions()),
            httpClientFactory.Object,
            OptionsMonitorOf(options),
            Mock.Of<IIdentityUserStore>(MockBehavior.Strict),
            Mock.Of<IExternalLoginStore>(MockBehavior.Strict),
            Mock.Of<ITenantMembershipStore>(MockBehavior.Strict),
            Mock.Of<ITenantProvisioningService>(MockBehavior.Strict),
            Mock.Of<ITokenService>(MockBehavior.Strict));

        var start = await sut.StartOAuthAsync(provider, redirectUri);

        await AssertThrowsAsync<IdentityValidationException>(() =>
            sut.CompleteOAuthAsync(new OAuthCallbackRequest(provider, start.State, "oauth-code", redirectUri)));
    }

    [TestMethod]
    public async Task CompleteOAuthAsync_MultipleTenantsNoDefault_ReturnsPreTenantSelection()
    {
        var userId = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        const string provider = "google";
        const string redirectUri = "https://localhost:5001/signin/oauth";

        var options = new OAuthOptions
        {
            StateTtlMinutes = 10,
            Providers = new Dictionary<string, OAuthProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                [provider] = new()
                {
                    Enabled = true,
                    ClientId = "client-id",
                    ClientSecret = "client-secret",
                    AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                    TokenEndpoint = "https://oauth2.googleapis.com/token",
                    UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo",
                    Scope = "openid profile email"
                }
            }
        };

        var handler = new QueueHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"oauth-access-token"}""", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"sub":"provider-user-1","email":"abram.cookson@outlook.com","name":"Abram","email_verified":true}""", Encoding.UTF8, "application/json")
            });

        var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var users = new Mock<IIdentityUserStore>(MockBehavior.Strict);
        users.Setup(x => x.FindByEmailAsync("abram.cookson@outlook.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityUser(userId, "abram.cookson@outlook.com", true));
        users.Setup(x => x.SetEmailConfirmedAsync(userId, true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var externalLogins = new Mock<IExternalLoginStore>(MockBehavior.Strict);
        externalLogins.Setup(x => x.FindByProviderAsync(provider, "provider-user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalLoginInfo?)null);
        externalLogins.Setup(x => x.LinkAsync(userId, provider, "provider-user-1", "abram.cookson@outlook.com", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tenants = new Mock<ITenantMembershipStore>(MockBehavior.Strict);
        tenants.Setup(x => x.GetTenantsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInfo>
            {
                new(tenantA, "Tenant A", new List<string> { "User" }, true),
                new(tenantB, "Tenant B", new List<string> { "Admin" }, true)
            });
        tenants.Setup(x => x.GetDefaultTenantIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        tokens.Setup(x => x.CreatePreTenantTokenAsync(
                userId,
                It.IsAny<IReadOnlyList<ClaimItem>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResult("pre-tenant-jwt", DateTimeOffset.UtcNow.AddMinutes(10), new List<ClaimItem>()));

        var sut = new OAuthAuthService(
            new MemoryCache(new MemoryCacheOptions()),
            httpClientFactory.Object,
            OptionsMonitorOf(options),
            users.Object,
            externalLogins.Object,
            tenants.Object,
            Mock.Of<ITenantProvisioningService>(MockBehavior.Strict),
            tokens.Object);

        var start = await sut.StartOAuthAsync(provider, redirectUri);
        var result = await sut.CompleteOAuthAsync(new OAuthCallbackRequest(provider, start.State, "oauth-code", redirectUri));

        Assert.IsTrue(result.RequiresTenantSelection);
        Assert.AreEqual("pre-tenant-jwt", result.PreTenantToken);
        Assert.AreEqual(2, result.Tenants.Count);
    }

    private static IOptionsMonitor<OAuthOptions> OptionsMonitorOf(OAuthOptions value)
    {
        var monitor = new Mock<IOptionsMonitor<OAuthOptions>>(MockBehavior.Strict);
        monitor.Setup(x => x.CurrentValue).Returns(value);
        return monitor.Object;
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected exception {typeof(TException).Name} was not thrown.");
            throw new InvalidOperationException("Unreachable");
        }
        catch (TException ex)
        {
            return ex;
        }
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No queued HTTP response remains.");

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
