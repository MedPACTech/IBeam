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
public sealed class SeatPortalTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task TransportUnknownReferencesCannotBeSubstitutedAcrossApplications(bool wrongScope)
    {
        var app = Guid.NewGuid(); var tenant = Guid.NewGuid(); var license = Guid.NewGuid(); var user = Guid.NewGuid(); var operation = Guid.NewGuid();
        var client = new Mock<IManagementOperationClient>();
        client.Setup(x => x.ExecuteAsync<ManagementSeatAssignRequest, ManagementSeatTransitionResult>(app, ManagementSeatOperations.Assign, tenant, license,
            It.IsAny<ManagementSeatAssignRequest>(), "seat-intent-123", "version-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ManagementOperationUncertainException(wrongScope ? Guid.NewGuid() : app, tenant, license, operation));
        var service = new ManagementSeatPortalService(client.Object, new ServiceOperationExecutor());
        if (wrongScope) await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => service.AssignAsync(app, tenant, new(license, user, "version-1"), "seat-intent-123"));
        else
        {
            var result = await service.AssignAsync(app, tenant, new(license, user, "version-1"), "seat-intent-123");
            Assert.AreEqual("unknown", result.Outcome); Assert.AreEqual(operation, result.OperationId); Assert.AreEqual(user, result.UserId);
            Assert.IsNull(result.AssignmentId); Assert.IsNull(result.Version); Assert.IsNull(result.Disposition);
        }
        client.Verify(x => x.ExecuteAsync<ManagementSeatAssignRequest, ManagementSeatTransitionResult>(app, ManagementSeatOperations.Assign, tenant, license,
            It.IsAny<ManagementSeatAssignRequest>(), "seat-intent-123", "version-1", It.IsAny<CancellationToken>()), Times.Once);
    }
    [TestMethod]
    public async Task PortalReadAssignAndUnassignRetainIndependentGrantsAndExactVersions()
    {
        await using var f = await HttpFixture.Create();
        var before = (await f.Client.GetFromJsonAsync<JsonElement>(f.Path)).GetProperty("data");
        using var response = await f.Write("assign", before.GetProperty("version").GetString()!, new { userId = f.Data.User });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode); Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
        var assigned = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.AreEqual("assigned", assigned.GetProperty("disposition").GetString());
        var body = new { userId = f.Data.User, assignmentId = assigned.GetProperty("assignmentId").GetGuid() };
        f.Data.Access.Human = [ManagementSeatOperations.Read, ManagementSeatOperations.Assign];
        using var denied = await f.Write("unassign", assigned.GetProperty("version").GetString()!, body);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        f.Data.Access.Human = SeatRegistrationTests.Grants;
        using var removed = await f.Write("unassign", assigned.GetProperty("version").GetString()!, body);
        Assert.AreEqual(HttpStatusCode.OK, removed.StatusCode);
        Assert.HasCount(0, (await f.Data.Target.ReadAsync(f.Data.Context)).Assignments);
        f.Client.DefaultRequestHeaders.Authorization = new("Bearer", "customer");
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await f.Client.GetAsync(f.Path)).StatusCode);
    }
    [TestMethod]
    public async Task InvalidPayloadAndWildcardVersionFailWithoutEchoingProfileFields()
    {
        await using var f = await HttpFixture.Create();
        using var invalid = await f.Write("assign", f.Data.Context.ResourceVersion!, new { userId = f.Data.User, profile = "PRIVATE-PROFILE-CANARY" });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.DoesNotContain("PRIVATE-PROFILE-CANARY", await invalid.Content.ReadAsStringAsync());
        using var wildcard = await f.Write("assign", "*", new { userId = f.Data.User }); Assert.AreEqual(HttpStatusCode.BadRequest, wildcard.StatusCode);
        Assert.AreEqual(0, f.Data.Store.Commits);
        using var stale = await f.Write("assign", "stale-version", new { userId = f.Data.User }); Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }
    [TestMethod]
    public async Task UnknownSeatReplyHasAuthorizedReadOnlyPollingAndNoDuplicateAllocation()
    {
        await using var f = await HttpFixture.Create(); f.Data.Store.LoseReply = true;
        using var response = await f.Write("assign", f.Data.Context.ResourceVersion!, new { userId = f.Data.User });
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode); Assert.IsNotNull(response.Headers.Location);
        Assert.DoesNotContain("PROVIDER-PRIVATE-CANARY", await response.Content.ReadAsStringAsync());
        using var polled = await f.Client.GetAsync(response.Headers.Location);
        Assert.AreEqual(HttpStatusCode.OK, polled.StatusCode); Assert.AreEqual(1, f.Data.Store.Commits);
        Assert.AreEqual("assigned", (await polled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("disposition").GetString());
        f.Data.Access.Revoked = true;
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Client.GetAsync(response.Headers.Location)).StatusCode);
        Assert.AreEqual(1, f.Data.Store.Commits);
    }
    [TestMethod]
    public async Task DefaultApiDoesNotExposeOptionalSeatEndpoints()
    {
        await using var f = await HttpFixture.Create(install: false); Assert.AreEqual(HttpStatusCode.NotFound, (await f.Client.GetAsync(f.Path)).StatusCode);
    }
    private sealed class HttpFixture : IAsyncDisposable
    {
        public SeatAdministrationTests.Fixture Data { get; private set; } = null!;
        public WebApplication App { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public string Path => $"/api/management/applications/{Data.Context.ApplicationId}/tenants/{Data.Context.TenantId}/licenses/{Data.Context.ResourceId}/seats";
        public static async Task<HttpFixture> Create(bool install = true)
        {
            var f = new HttpFixture { Data = await SeatAdministrationTests.Fixture.Create() };
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["IBeam:Management:Api:AuthenticationScheme"] = "management" });
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, Auth>("management", _ => { });
            builder.Services.AddSingleton<IManagementOperationClient>(new Port(f.Data)); builder.Services.AddIBeamManagementSeatsPortal();
            if (install) builder.Services.AddIBeamManagementPortalSeatsApi(builder.Configuration); else builder.Services.AddIBeamManagementApi(builder.Configuration);
            f.App = builder.Build(); f.App.UseAuthentication(); f.App.UseAuthorization(); f.App.MapControllers(); await f.App.StartAsync();
            f.Client = f.App.GetTestClient(); f.Client.DefaultRequestHeaders.Authorization = new("Bearer", "support"); return f;
        }
        public async Task<HttpResponseMessage> Write(string action, string version, object body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Path + "/" + action) { Content = JsonContent.Create(body) };
            request.Headers.TryAddWithoutValidation("If-Match", version); request.Headers.Add("Idempotency-Key", "fixture-" + action); return await Client.SendAsync(request);
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
    private sealed class Port(SeatAdministrationTests.Fixture f) : IManagementOperationClient
    {
        private Guid lastId;
        public async Task<T> ExecuteAsync<R, T>(Guid app, string operation, Guid tenant, Guid resource, R request, string key, string? version, CancellationToken ct = default)
        {
            var context = f.Context with { ApplicationId = app, TenantId = tenant, ResourceId = resource, ResourceVersion = version,
                OperationId = operation == ManagementSeatOperations.Read ? Guid.Empty : lastId = Guid.NewGuid() };
            if (operation == ManagementSeatOperations.Read) return (T)(object)await f.Target.ReadAsync(context, ct);
            var user = request is ManagementSeatAssignRequest assign ? assign.UserId : ((ManagementSeatUnassignRequest)(object)request!).UserId;
            Guid? assignment = request is ManagementSeatUnassignRequest unassign ? unassign.AssignmentId : null;
            return (T)(object)await f.Target.ChangeAsync(operation, context, resource, user, assignment, version!, ct);
        }
        public async Task<T> ReadReceiptAsync<T>(Guid app, string operation, Guid tenant, Guid resource, Guid id, CancellationToken ct = default)
        {
            if (id != lastId) throw new ManagementAccessDeniedException();
            return (T)(object)await f.Target.FindAsync(operation, f.Context with { ApplicationId = app, TenantId = tenant, ResourceId = resource, OperationId = id }, ct);
        }
    }
}
