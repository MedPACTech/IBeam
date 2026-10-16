using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Api;
using IBeam.Management.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class ConnectionApiTests
{
    [TestMethod]
    public async Task OptionalRoutesRequireSecretsAndExplicitApprovedOriginsAtStartup()
    {
        await using var disabled = await Host(install: false);
        using var client = disabled.GetTestClient();
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsync("/api/management/applications/" + Guid.NewGuid() + "/connection/test", null)).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var app = await Host(secretStore: false); });
        await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(async () => { await using var app = await Host(approved: false); });
    }
    [TestMethod]
    public async Task HttpKeysAreWriteOnlyAndBodyLoggingIsSuppressedEvenWhenHostEnablesIt()
    {
        var logs = new CapturedLogs();
        await using var app = await Host(logs: logs);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer admin");
        var created = await client.PostAsJsonAsync("/api/management/applications", ApplicationFixture.Request());
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        var route = "/api/management/applications/" + data.GetProperty("applicationId").GetGuid() + "/connection";
        var version = data.GetProperty("version").GetString()!;
        using var result = await client.PutAsJsonAsync(route, new { apiKey = ConnectionFixture.Key, expectedVersion = version });
        Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
        var content = await result.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ConnectionFixture.Key, content);
        Assert.DoesNotContain("secretReference", content);
        Assert.AreEqual("no-store", result.Headers.CacheControl?.ToString());
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync(route + "/test", new { url = "https://evil.example" })).StatusCode);
        var invalid = await client.PutAsync(route, new StringContent("{\"apiKey\":\"" + ConnectionFixture.Key + "\",\"expectedVersion\":{}}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.DoesNotContain(ConnectionFixture.Key, await invalid.Content.ReadAsStringAsync());
        Assert.DoesNotContain(ConnectionFixture.Key, string.Join("\n", logs.Messages));
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.Add("Authorization", "Bearer reader");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(route, new { apiKey = ConnectionFixture.Key, expectedVersion = version })).StatusCode);
    }
    [TestMethod]
    [DataRow(false, 503)]
    [DataRow(true, 504)]
    public async Task OutagesAndTimeoutsUseSafeEnvelopes(bool timeout, int expected)
    {
        await using var app = await Host();
        using var client = app.GetTestClient(); client.DefaultRequestHeaders.Add("Authorization", "Bearer admin");
        var created = await client.PostAsJsonAsync("/api/management/applications", ApplicationFixture.Request());
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        app.Services.GetRequiredService<FakeProbe>().Failure = timeout ? new ManagementConnectionTimeoutException() : new Exception(ConnectionFixture.Key);
        var response = await client.PutAsJsonAsync("/api/management/applications/" + data.GetProperty("applicationId").GetGuid() + "/connection",
            new { apiKey = ConnectionFixture.Key, expectedVersion = data.GetProperty("version").GetString() });
        Assert.AreEqual(expected, (int)response.StatusCode);
        Assert.DoesNotContain(ConnectionFixture.Key, await response.Content.ReadAsStringAsync());
    }
    private static async Task<WebApplication> Host(bool install = true, bool secretStore = true, bool approved = true, CapturedLogs? logs = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders(); builder.Logging.SetMinimumLevel(LogLevel.Trace);
        if (logs is not null) builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { ["IBeam:Management:Connections:ApprovedOrigins:0"] = approved ? "https://api.qurvia.app" : "" });
        builder.Services.AddAuthentication("Customer").AddScheme<AuthenticationSchemeOptions, AppAuthentication>("Customer", _ => {})
            .AddScheme<AuthenticationSchemeOptions, AppAuthentication>("Support", _ => {});
        builder.Services.AddControllers().ConfigureApplicationPartManager(x => x.ApplicationParts.Clear());
        builder.Services.AddIBeamManagementApi(builder.Configuration, x => x.AuthenticationScheme = "Support");
        if (install) builder.Services.AddIBeamManagementConnectionsApi(builder.Configuration);
        builder.Services.AddSingleton<IManagementApplicationRepository, MemoryApplications>();
        builder.Services.AddSingleton<IManagementApplicationCredentialRepository, MemoryApplicationCredentials>();
        if (secretStore) builder.Services.AddSingleton<IManagementSecretStore, MemorySecrets>();
        builder.Services.AddSingleton<FakeProbe>(); builder.Services.AddSingleton<IManagementTargetConnectionProbe>(p => p.GetRequiredService<FakeProbe>());
        builder.Services.AddHttpLogging(x => { x.LoggingFields = HttpLoggingFields.All; x.RequestBodyLogLimit = 16384; });
        var app = builder.Build();
        app.UseHttpLogging(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        try { await app.StartAsync(); } catch { await app.DisposeAsync(); throw; }
        return app;
    }
    private sealed class CapturedLogs : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                { lock (messages) messages.Add(formatter(state, exception)); }
        }
    }
}
