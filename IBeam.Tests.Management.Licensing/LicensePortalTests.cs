using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Api;
using IBeam.Management.Licensing;
using IBeam.Management.Operations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IBeam.Services.Abstractions;
using Moq;

namespace IBeam.Tests.Management.Licensing;

[TestClass]
public sealed class LicensePortalTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task TransportUncertaintyKeepsOnlyTheOriginalScopedOperationReference(bool substitutedScope)
    {
        var app = Guid.NewGuid(); var tenant = Guid.NewGuid(); var license = Guid.NewGuid(); var operation = Guid.NewGuid();
        var client = new Mock<IManagementOperationClient>();
        client.Setup(x => x.ExecuteAsync<ManagementLicenseSuspendRequest, ManagementLicenseTransitionResult>(app, ManagementLicenseOperations.Suspend,
            tenant, license, It.IsAny<ManagementLicenseSuspendRequest>(), "intent-12345", "version-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ManagementOperationUncertainException(substitutedScope ? Guid.NewGuid() : app, tenant, license, operation));
        var service = new ManagementLicensePortalService(client.Object, new ServiceOperationExecutor());
        if (substitutedScope) await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => service.SuspendAsync(app, tenant, new(license, "version-1"), "intent-12345"));
        else
        {
            var result = await service.SuspendAsync(app, tenant, new(license, "version-1"), "intent-12345");
            Assert.AreEqual("unknown", result.Outcome); Assert.AreEqual(operation, result.OperationId);
            Assert.IsNull(result.Status); Assert.IsNull(result.Version);
        }
        client.Verify(x => x.ExecuteAsync<ManagementLicenseSuspendRequest, ManagementLicenseTransitionResult>(app, ManagementLicenseOperations.Suspend,
            tenant, license, It.IsAny<ManagementLicenseSuspendRequest>(), "intent-12345", "version-1", It.IsAny<CancellationToken>()), Times.Once);
    }
    [TestMethod]
    public async Task TypedPortalEndpointsRequireNamedAuthExactVersionsAndIndependentGrants()
    {
        await using var fixture = await HttpFixture.Create();
        var read = await fixture.Client.GetFromJsonAsync<JsonElement>(fixture.Path);
        var version = read.GetProperty("data").GetProperty("version").GetString()!;
        using var suspended = await fixture.Write("suspend", version);
        Assert.AreEqual(HttpStatusCode.OK, suspended.StatusCode);
        Assert.AreEqual("no-store", suspended.Headers.CacheControl!.ToString());
        var changed = (await suspended.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.AreEqual("suspended", changed.GetProperty("status").GetString());
        Assert.AreEqual(ManagementLicenseOperations.Suspend, fixture.Port.LastOperation);
        fixture.Data.Access.Human = [ManagementLicenseOperations.Read, ManagementLicenseOperations.Suspend];
        using var denied = await fixture.Write("restore", changed.GetProperty("version").GetString()!);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        fixture.Data.Access.Human = [ManagementLicenseOperations.Read, ManagementLicenseOperations.Suspend, ManagementLicenseOperations.Restore];
        using var restored = await fixture.Write("restore", changed.GetProperty("version").GetString()!);
        Assert.AreEqual(HttpStatusCode.OK, restored.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, fixture.Path);
        request.Headers.Add("Authorization", "Bearer customer");
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        using var unauthorized = await fixture.Client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [TestMethod]
    public async Task StrictBodyAndWildcardHeadersFailWithoutEchoingPrivateInput()
    {
        await using var f = await HttpFixture.Create();
        using var bad = await f.Write("suspend", f.Data.Context.ResourceVersion!, new { privateCanary = "PROFILE-PRIVATE-CANARY" });
        Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.DoesNotContain("PROFILE-PRIVATE-CANARY", await bad.Content.ReadAsStringAsync());
        using var wildcard = await f.Write("suspend", "*");
        Assert.AreEqual(HttpStatusCode.BadRequest, wildcard.StatusCode);
        Assert.AreEqual(0, f.Data.Store.Commits);
        using var stale = await f.Write("suspend", "stale-version");
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.AreEqual(0, f.Data.Store.Commits);
    }

    [TestMethod]
    public async Task UnknownReplyReturnsAcceptedAndAnAuthorizedReadOnlyLocation()
    {
        await using var f = await HttpFixture.Create(); f.Data.Store.LoseReply = true;
        using var response = await f.Write("suspend", f.Data.Context.ResourceVersion!);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode); Assert.IsNotNull(response.Headers.Location);
        Assert.DoesNotContain("PROVIDER-PRIVATE-CANARY", await response.Content.ReadAsStringAsync());
        using var polled = await f.Client.GetAsync(response.Headers.Location);
        Assert.AreEqual(HttpStatusCode.OK, polled.StatusCode);
        Assert.AreEqual("succeeded", (await polled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("outcome").GetString());
        Assert.AreEqual(1, f.Data.Store.Commits);
        f.Data.Access.Revoked = true;
        using var denied = await f.Client.GetAsync(response.Headers.Location);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); Assert.AreEqual(1, f.Data.Store.Commits);
    }

    [TestMethod]
    public async Task BaseManagementApiDoesNotExposeOptionalLicenseControllers()
    {
        await using var f = await HttpFixture.Create(install: false);
        Assert.AreEqual(HttpStatusCode.NotFound, (await f.Client.GetAsync(f.Path)).StatusCode);
    }

    private sealed class HttpFixture : IAsyncDisposable
    {
        public LicenseStatusTests.Fixture Data { get; private set; } = null!;
        public Port Port { get; private set; } = null!;
        public WebApplication App { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public string Path => $"/api/management/applications/{Data.Context.ApplicationId}/tenants/{Data.Context.TenantId}/licenses/{Data.Context.ResourceId}";
        public static async Task<HttpFixture> Create(bool install = true)
        {
            var f = new HttpFixture { Data = await LicenseStatusTests.Fixture.Create() };
            f.Port = new(f.Data);
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["IBeam:Management:Api:AuthenticationScheme"] = "management" });
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, Auth>("management", _ => { });
            builder.Services.AddSingleton<IManagementOperationClient>(f.Port);
            builder.Services.AddIBeamManagementLicensePortal();
            if (install) builder.Services.AddIBeamManagementPortalLicenseApi(builder.Configuration);
            else builder.Services.AddIBeamManagementApi(builder.Configuration);
            f.App = builder.Build(); f.App.UseAuthentication(); f.App.UseAuthorization(); f.App.MapControllers();
            await f.App.StartAsync(); f.Client = f.App.GetTestClient(); f.Client.DefaultRequestHeaders.Add("Authorization", "Bearer support");
            return f;
        }
        public async Task<HttpResponseMessage> Write(string action, string version, object? body = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Path + "/" + action) { Content = JsonContent.Create(body ?? new { }) };
            request.Headers.TryAddWithoutValidation("If-Match", version); request.Headers.Add("Idempotency-Key", "fixture-" + action);
            return await Client.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.Authorization == "Bearer support"
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "fixture-support")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.Fail("Untrusted support credential."));
    }
    private sealed class Port(LicenseStatusTests.Fixture fixture) : IManagementOperationClient
    {
        public string? LastOperation { get; private set; }
        private Guid lastId;
        public async Task<T> ExecuteAsync<R, T>(Guid app, string operation, Guid tenant, Guid resource, R request, string key, string? version, CancellationToken ct = default)
        {
            LastOperation = operation;
            var context = fixture.Context with { ApplicationId = app, TenantId = tenant, ResourceId = resource, ResourceVersion = version,
                OperationId = operation == ManagementLicenseOperations.Read ? Guid.Empty : lastId = Guid.NewGuid() };
            if (operation == ManagementLicenseOperations.Read) return (T)(object)await fixture.Target.ReadAsync(context, ct);
            return (T)(object)await fixture.Target.ChangeAsync(operation, context, resource, version!, ct);
        }
        public async Task<T> ReadReceiptAsync<T>(Guid app, string operation, Guid tenant, Guid resource, Guid id, CancellationToken ct = default)
        {
            if (id != lastId) throw new ManagementAccessDeniedException();
            return (T)(object)await fixture.Target.FindAsync(operation, fixture.Context with { ApplicationId = app, TenantId = tenant, ResourceId = resource, OperationId = id }, ct);
        }
    }
}
