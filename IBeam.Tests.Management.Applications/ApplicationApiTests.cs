using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Applications;
[TestClass]
public sealed class ApplicationApiTests
{
    [TestMethod]
    public async Task RegistryRoutesAreOptionalAndMissingStoreFailsStartup()
    {
        await using var disabled = await Host(false);
        using var client = disabled.GetTestClient();
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/api/management/applications")).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var broken = await Host(true, false); });
    }
    [TestMethod]
    [DataRow(null, 401)]
    [DataRow("customer", 401)]
    [DataRow("reader", 403)]
    [DataRow("admin", 200)]
    public async Task ExplicitSupportSchemeAndPermissionAreRequired(string? user, int status)
    {
        await using var app = await Host();
        using var client = app.GetTestClient();
        if (user is not null) client.DefaultRequestHeaders.Add("Authorization", "Bearer " + user);
        Assert.AreEqual(status, (int)(await client.GetAsync("/api/management/applications")).StatusCode);
    }
    [TestMethod]
    public async Task CreateReadPatchDuplicateAndBadModelUseSafeEnvelopes()
    {
        await using var app = await Host();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer admin");
        var json = JsonSerializer.Serialize(ApplicationFixture.Request(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json = json[..^1] + ",\"organizationId\":\"" + Guid.NewGuid() + "\"}";
        using var created = await client.PostAsync("/api/management/applications", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        Assert.AreEqual("no-store", created.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        var id = data.GetProperty("applicationId").GetGuid();
        Assert.IsNotNull(created.Headers.Location);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync(created.Headers.Location)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/management/applications", ApplicationFixture.Request())).StatusCode);
        var update = new UpdateManagementApplication { Name = "Renamed", BaseUrl = "https://api.qurvia.app", IsEnabled = false,
            ExpectedVersion = data.GetProperty("version").GetString()! };
        Assert.AreEqual(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/api/management/applications/" + id, update)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync("/api/management/applications/" + id, update)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/api/management/applications/" + Guid.NewGuid())).StatusCode);
        var invalid = await client.GetAsync("/api/management/applications?pageSize=secret-do-not-echo");
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.DoesNotContain("secret-do-not-echo", await invalid.Content.ReadAsStringAsync());
    }
    private static async Task<WebApplication> Host(bool install = true, bool repository = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Customer")
            .AddScheme<AuthenticationSchemeOptions, AppAuthentication>("Customer", _ => {})
            .AddScheme<AuthenticationSchemeOptions, AppAuthentication>("Support", _ => {});
        builder.Services.AddControllers().ConfigureApplicationPartManager(x => x.ApplicationParts.Clear());
        builder.Services.AddIBeamManagementApi(builder.Configuration, x => x.AuthenticationScheme = "Support");
        if (install) builder.Services.AddIBeamManagementApplicationsApi();
        if (repository) builder.Services.AddSingleton<IManagementApplicationRepository, MemoryApplications>();
        var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        try { await app.StartAsync(); } catch { await app.DisposeAsync(); throw; }
        return app;
    }
}
internal sealed class AppAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
        if (Scheme.Name != "Support" || user is not ("reader" or "admin")) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new("sub", "operator"), new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human),
            new(ManagementClaimTypes.OrganizationId, "63e7b418-1856-4eb0-a24f-53cd027d962e") };
        if (user == "admin") claims.Add(new(ManagementClaimTypes.Permission, ManagementApplicationOperations.Admin));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
