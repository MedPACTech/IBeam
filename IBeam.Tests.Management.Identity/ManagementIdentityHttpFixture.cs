using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Schema;
using IBeam.Management.Identity.Api;
using IBeam.Management.Identity.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IBeam.Tests.Management.Identity;

internal sealed class ManagementIdentityHttpFixture : IAsyncDisposable
{
    public const string Email = "administrator@example.test";
    public const string Password = "Fixture-Password-Only!94";
    public Guid OrganizationId { get; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public string Prefix { get; } = "Auth" + Guid.NewGuid().ToString("N")[..12];
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public string Issuer => "ManagementFixture";
    public string Audience => "management-fixture";
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public CapturingSender Sender { get; } = new();
    private TableServiceClient _storage = null!;

    public static async Task<ManagementIdentityHttpFixture> CreateAsync()
    {
        var fixture = new ManagementIdentityHttpFixture();
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Set IBEAM_MANAGEMENT_TEST_STORAGE to private Azurite.");
        fixture._storage = new TableServiceClient(connection);
        var values = new Dictionary<string, string?>
        {
            ["IBeam:Management:Identity:OrganizationId"] = fixture.OrganizationId.ToString(),
            ["IBeam:Management:Identity:BootstrapEnabled"] = "true",
            ["IBeam:Management:Identity:Storage:Mode"] = "Separate",
            ["IBeam:Management:Identity:Storage:ConnectionString"] = connection,
            ["IBeam:Management:Identity:Storage:TablePrefix"] = fixture.Prefix,
            ["IBeam:Management:Identity:Storage:ProvisionSchema"] = "true",
            ["IBeam:Management:Identity:Jwt:Issuer"] = fixture.Issuer,
            ["IBeam:Management:Identity:Jwt:Audience"] = fixture.Audience,
            ["IBeam:Management:Identity:Jwt:SigningKey"] = fixture.SigningKey,
            ["IBeam:Management:Identity:Otp:HashSalt"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["IBeam:Management:Identity:Otp:VerificationTokenSecret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["IBeam:Management:Identity:Otp:FailureResponseDelayMilliseconds"] = "0"
        };
        var setupConfiguration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var setup = new ServiceCollection();
        setup.AddLogging();
        setup.AddHttpContextAccessor();
        setup.AddDataProtection().UseEphemeralDataProtectionProvider();
        setup.AddIBeamManagementIdentityServices(setupConfiguration);
        await using (var provider = setup.BuildServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IIdentitySchemaManager>().ApplyAsync();
            var result = await scope.ServiceProvider.GetRequiredService<IManagementIdentityBootstrapService>().BootstrapAsync(
                new ManagementBootstrapRequest { OrganizationName = "Fixture support", AdministratorEmail = Email, InitialPassword = Password });
            fixture.UserId = result.AdministratorUserId;
        }

        values["IBeam:Management:Identity:BootstrapEnabled"] = "false";
        values["IBeam:Management:Identity:Storage:ProvisionSchema"] = "false";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(values);
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton<IIdentityCommunicationSender>(fixture.Sender);
        builder.Services.AddControllers().ConfigureApplicationPartManager(parts => parts.ApplicationParts.Clear());
        builder.Services.AddIBeamManagementIdentityApi(builder.Configuration);
        fixture.App = builder.Build();
        fixture.App.UseAuthentication();
        fixture.App.UseAuthorization();
        fixture.App.MapControllers();
        await fixture.App.StartAsync();
        fixture.Client = fixture.App.GetTestClient();
        return fixture;
    }

    public async Task<TokenResult> LoginAsync(string email = Email, string password = Password)
    {
        using var challengeResponse = await Client.PostAsJsonAsync("/api/auth/password-login", new { email, password });
        Assert.AreEqual(200, (int)challengeResponse.StatusCode);
        var challenge = await ReadDataAsync<AuthResultResponse>(challengeResponse);
        Assert.IsTrue(challenge.RequiresTwoFactor);
        Assert.IsNull(challenge.Token);
        Assert.IsNull(challenge.PreTenantToken);
        using var complete = await Client.PostAsJsonAsync("/api/auth/2fa/complete-login", new
        {
            email, challengeId = challenge.TwoFactorChallengeId, code = Sender.Messages.Last(x => x.Destination == email).Code
        });
        Assert.AreEqual(200, (int)complete.StatusCode);
        return (await ReadDataAsync<AuthResultResponse>(complete)).Token!;
    }

    public async Task<HttpResponseMessage> GetIdentityAsync(string? accessToken = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/management/me");
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = App.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return JsonSerializer.Deserialize<T>(document.RootElement.GetProperty("data").GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null) await App.DisposeAsync();
        if (_storage is not null)
            await foreach (var table in _storage.QueryAsync())
                if (table.Name.StartsWith(Prefix, StringComparison.Ordinal)) await _storage.DeleteTableAsync(table.Name);
    }

    internal sealed class CapturingSender : IIdentityCommunicationSender
    {
        public List<IdentitySenderMessage> Messages { get; } = [];
        public Task SendAsync(IdentitySenderMessage message, CancellationToken ct = default)
        { Messages.Add(message); return Task.CompletedTask; }
    }
}
