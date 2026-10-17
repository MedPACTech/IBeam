using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Api;
using IBeam.Management.Diagnostics.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management;

[TestClass]
public sealed class ManagementApiBoundaryTests
{
    internal static readonly Guid OrganizationId = Guid.Parse("aa18fde8-40cb-47c6-821b-3d5817dc6767");
    internal static readonly Guid CredentialId = Guid.Parse("2283574d-eb26-4c17-b3b6-65cbbdefc47c");

    [TestMethod]
    [DataRow(null, 401)]
    [DataRow("customer", 401)]
    [DataRow("key", 401)]
    [DataRow("support", 200)]
    [DataRow("support-machine", 403)]
    [DataRow("support-no-org", 403)]
    [DataRow("support-empty-org", 403)]
    [DataRow("support-duplicate-org", 403)]
    [DataRow("support-no-permission", 403)]
    [DataRow("support-no-user", 403)]
    [DataRow("support-conflicting-user", 403)]
    [DataRow("support-owner-only", 403)]
    public async Task PortalRequiresNamedSupportSchemeAndExplicitContext(string? token, int expected)
    {
        await using var app = await StartHostAsync(diagnostics: false);
        using var client = app.GetTestClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/management/me");
        Assert.AreEqual(expected, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (expected == 200)
        {
            using var json = JsonDocument.Parse(body);
            Assert.IsTrue(json.RootElement.GetProperty("success").GetBoolean());
            Assert.AreEqual("support-user", json.RootElement.GetProperty("data").GetProperty("userId").GetString());
            Assert.AreEqual(OrganizationId, json.RootElement.GetProperty("data").GetProperty("managementOrganizationId").GetGuid());
            Assert.IsFalse(body.Contains(ManagementClaimTypes.Permission, StringComparison.Ordinal));
        }
        if (expected == 403)
        {
            Assert.IsTrue(body.Contains("Management.AccessDenied", StringComparison.Ordinal));
            Assert.IsFalse(body.Contains(OrganizationId.ToString(), StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow(null, 401)]
    [DataRow("customer", 401)]
    [DataRow("support", 401)]
    [DataRow("key", 200)]
    [DataRow("key-wrong-app", 403)]
    [DataRow("key-wrong-env", 403)]
    [DataRow("key-human", 403)]
    [DataRow("key-no-permission", 403)]
    [DataRow("key-no-id", 403)]
    [DataRow("key-empty-id", 403)]
    [DataRow("key-duplicate-app", 403)]
    public async Task TargetRequiresMachineSchemeCapabilityAndExactDeployment(string? token, int expected)
    {
        await using var app = await StartHostAsync(diagnostics: true);
        using var client = app.GetTestClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/management-diagnostics/info");
        Assert.AreEqual(expected, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (expected == 200)
        {
            using var json = JsonDocument.Parse(body);
            var data = json.RootElement.GetProperty("data");
            Assert.AreEqual("qurvia", data.GetProperty("applicationKey").GetString());
            Assert.AreEqual("Production", data.GetProperty("environment").GetString());
            var capabilities = data.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).ToArray();
            CollectionAssert.AreEqual(new[] { ManagementOperations.DiagnosticsInfo }, capabilities);
            Assert.IsFalse(body.Contains(CredentialId.ToString(), StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task RegisteringManagementNeverChangesConsumerAuthenticationDefault()
    {
        await using var app = await StartHostAsync(false);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "customer");
        using var customerResponse = await client.GetAsync("/customer");
        Assert.AreEqual(HttpStatusCode.OK, customerResponse.StatusCode);
        using var managementResponse = await client.GetAsync("/api/management/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, managementResponse.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "support");
        using var supportOnCustomer = await client.GetAsync("/customer");
        Assert.AreEqual(HttpStatusCode.Unauthorized, supportOnCustomer.StatusCode);
    }

    [TestMethod]
    [DataRow(false, "")]
    [DataRow(false, "NotRegistered")]
    [DataRow(true, "")]
    [DataRow(true, "NotRegistered")]
    public async Task UnconfiguredOrMissingAuthenticationSchemeFailsAtStartup(bool diagnostics, string scheme)
    {
        if (string.IsNullOrEmpty(scheme))
            await Assert.ThrowsAsync<OptionsValidationException>(
                async () => { await using var app = await StartHostAsync(diagnostics, scheme); });
        else
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => { await using var app = await StartHostAsync(diagnostics, scheme); });
    }

    [TestMethod]
    [DataRow("", "Production")]
    [DataRow("qurvia", "")]
    public async Task MissingDeploymentBindingFailsAtStartup(string appKey, string environment)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(
            async () => { await using var app = await StartHostAsync(true, appKey: appKey, environment: environment); });
    }

    private static async Task<WebApplication> StartHostAsync(
        bool diagnostics, string? scheme = null, string appKey = "qurvia", string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ManagementApiOptions.SectionName + ":AuthenticationScheme"] = scheme ?? "Support",
            [ManagementDiagnosticsApiOptions.SectionName + ":AuthenticationScheme"] = scheme ?? "ManagementKey",
            [ManagementDiagnosticsOptions.SectionName + ":ApplicationKey"] = appKey,
            [ManagementDiagnosticsOptions.SectionName + ":Environment"] = environment
        });
        builder.Services.AddAuthentication("Customer")
            .AddScheme<AuthenticationSchemeOptions, BoundaryAuthenticationHandler>("Customer", _ => { })
            .AddScheme<AuthenticationSchemeOptions, BoundaryAuthenticationHandler>("Support", _ => { })
            .AddScheme<AuthenticationSchemeOptions, BoundaryAuthenticationHandler>("ManagementKey", _ => { });
        // Test project references both API packages; install only the module selected by this host.
        builder.Services.AddControllers().ConfigureApplicationPartManager(parts => parts.ApplicationParts.Clear());
        if (diagnostics) builder.Services.AddIBeamManagementDiagnosticsApi(builder.Configuration);
        else builder.Services.AddIBeamManagementApi(builder.Configuration);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/customer", () => Results.Ok()).RequireAuthorization();
        try { await app.StartAsync(); }
        catch { await app.DisposeAsync(); throw; }
        return app;
    }

    internal static List<Claim> SupportClaims() =>
    [
        new("sub", "support-user"),
        new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human),
        new(ManagementClaimTypes.OrganizationId, OrganizationId.ToString()),
        new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead)
    ];

    internal static List<Claim> KeyClaims() =>
    [
        new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Credential),
        new(ManagementClaimTypes.CredentialId, CredentialId.ToString()),
        new(ManagementClaimTypes.ApplicationKey, "qurvia"),
        new(ManagementClaimTypes.Environment, "Production"),
        new(ManagementClaimTypes.Permission, ManagementOperations.DiagnosticsInfo)
    ];
}

internal sealed class BoundaryAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal);
        List<Claim>? claims = null;
        if (Scheme.Name == "Customer" && token == "customer")
        {
            // Even matching context claims from the wrong authentication trust domain must fail.
            claims = ManagementApiBoundaryTests.SupportClaims();
        }
        if (Scheme.Name == "Support" && token.StartsWith("support", StringComparison.Ordinal))
        {
            claims = ManagementApiBoundaryTests.SupportClaims();
            if (token == "support-machine") Replace(claims, ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Credential);
            if (token == "support-no-org") claims.RemoveAll(c => c.Type == ManagementClaimTypes.OrganizationId);
            if (token == "support-empty-org") Replace(claims, ManagementClaimTypes.OrganizationId, Guid.Empty.ToString());
            if (token == "support-duplicate-org") claims.Add(new(ManagementClaimTypes.OrganizationId, Guid.NewGuid().ToString()));
            if (token == "support-no-permission") claims.RemoveAll(c => c.Type == ManagementClaimTypes.Permission);
            if (token == "support-no-user") claims.RemoveAll(c => c.Type == "sub");
            if (token == "support-conflicting-user") claims.Add(new("uid", "another-user"));
            if (token == "support-owner-only")
                claims = [new("sub", "support-user"), new(ClaimTypes.Role, "Owner")];
        }
        if (Scheme.Name == "ManagementKey" && token.StartsWith("key", StringComparison.Ordinal))
        {
            claims = ManagementApiBoundaryTests.KeyClaims();
            if (token == "key-wrong-app") Replace(claims, ManagementClaimTypes.ApplicationKey, "bindry");
            if (token == "key-wrong-env") Replace(claims, ManagementClaimTypes.Environment, "Staging");
            if (token == "key-human") Replace(claims, ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human);
            if (token == "key-no-permission") claims.RemoveAll(c => c.Type == ManagementClaimTypes.Permission);
            if (token == "key-no-id") claims.RemoveAll(c => c.Type == ManagementClaimTypes.CredentialId);
            if (token == "key-empty-id") Replace(claims, ManagementClaimTypes.CredentialId, Guid.Empty.ToString());
            if (token == "key-duplicate-app") claims.Add(new(ManagementClaimTypes.ApplicationKey, "bindry"));
        }
        return Task.FromResult(claims is null ? AuthenticateResult.NoResult() :
            AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }

    private static void Replace(List<Claim> claims, string type, string value)
    {
        claims.RemoveAll(c => c.Type == type);
        claims.Add(new(type, value));
    }
}
