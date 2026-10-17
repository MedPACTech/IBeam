using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IBeam.Management;
using IBeam.Management.Diagnostics.Api.Authentication;
using IBeam.Services.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Credentials;

internal sealed class CredentialApiFixture : IAsyncDisposable
{
    public CredentialFixture Credentials { get; }
    private CredentialApiFixture(IManagementApiCredentialRepository? repository)
    {
        Credentials = new CredentialFixture(customize: services =>
        {
            if (repository is not null) services.AddSingleton(repository);
        });
    }
    public RecordingCredentialAuditSink Audits { get; } = new();
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public static async Task<CredentialApiFixture> CreateAsync(
        string? allowedNetwork = null, string? remoteAddress = "127.0.0.1",
        bool customerScheme = true, Func<CredentialApiFixture, HttpContext, Task>? afterAuthentication = null, IManagementApiCredentialRepository? repository = null)
    {
        var fixture = new CredentialApiFixture(repository);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        var values = new Dictionary<string, string?>
        {
            ["IBeam:Management:Diagnostics:ApplicationKey"] = "qurvia",
            ["IBeam:Management:Diagnostics:Environment"] = "Production"
        };
        if (allowedNetwork is not null) values["IBeam:Management:Credentials:Network:AllowedNetworks:0"] = allowedNetwork;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddSingleton(fixture.Credentials.Provider.GetRequiredService<IManagementApiCredentialRepository>());
        builder.Services.AddSingleton<IAuditTrailSink>(fixture.Audits);
        builder.Services.Configure<ServiceAuditOptions>(o => o.Enabled = true);
        builder.Services.AddControllers().ConfigureApplicationPartManager(p => p.ApplicationParts.Clear());
        if (customerScheme)
            builder.Services.AddAuthentication("Customer").AddScheme<AuthenticationSchemeOptions, CustomerAuthenticationHandler>("Customer", _ => { });
        builder.Services.AddIBeamManagementDiagnosticsCredentialApi(builder.Configuration);
        fixture.App = builder.Build();
        fixture.App.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = remoteAddress is null ? null : IPAddress.Parse(remoteAddress);
            context.TraceIdentifier = "management-fixture:" + Guid.NewGuid().ToString("N");
            try { await next(); }
            catch (InvalidOperationException)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsync("Service unavailable.");
            }
        });
        fixture.App.UseAuthentication();
        fixture.App.Use(async (context, next) =>
        {
            if (afterAuthentication is not null) await afterAuthentication(fixture, context);
            await next();
        });
        fixture.App.UseAuthorization();
        fixture.App.MapControllers();
        fixture.App.MapGet("/customer-data", () => Results.Ok("customer-only")).RequireAuthorization();
        try
        {
            await fixture.App.StartAsync();
            fixture.Client = fixture.App.GetTestClient();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task<HttpResponseMessage> GetInfoAsync(string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/management-diagnostics/info");
        if (key is not null) request.Headers.TryAddWithoutValidation(ManagementCredentialAuthenticationDefaults.HeaderName, key);
        return await Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null) await App.DisposeAsync();
        await Credentials.DisposeAsync();
    }

    private sealed class CustomerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization != "Bearer customer") return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "customer-user")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}

internal sealed class RecordingCredentialAuditSink : IAuditTrailSink
{
    public ConcurrentQueue<ServiceAuditTransaction> Transactions { get; } = new();
    public Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default)
    { Transactions.Enqueue(transaction); return Task.CompletedTask; }
    public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default) => Task.CompletedTask;
}
