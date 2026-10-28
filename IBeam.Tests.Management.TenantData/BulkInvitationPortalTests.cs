using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IBeam.Management.Api;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class BulkInvitationPortalTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task BulkPortalRoutesAreOptInAndReturnOriginalReadonlyReceiptLocation(bool install)
    {
        await using var f = await Fixture.Create(install); using var request = f.Request("support"); using var response = await f.Http.SendAsync(request);
        if (!install) { Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode); Assert.AreEqual(0, f.Commands); return; }
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode); Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString()); Assert.IsNotNull(response.Headers.Location);
        Assert.IsTrue(response.Headers.Location.ToString().Contains(f.Operation.ToString("D"), StringComparison.Ordinal));
        using var poll = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location); poll.Headers.Authorization = new("Bearer", "support"); using var found = await f.Http.SendAsync(poll);
        Assert.AreEqual(HttpStatusCode.OK, found.StatusCode); Assert.AreEqual(1, f.Commands); Assert.AreEqual(1, f.Reads);
    }
    [TestMethod]
    public async Task BulkPortalDoesNotAcceptACustomerTokenAsItsManagementLogin()
    {
        await using var f = await Fixture.Create(true); using var request = f.Request("customer"); using var response = await f.Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode); Assert.AreEqual(0, f.Commands); Assert.AreEqual(0, f.Reads);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Application = Guid.NewGuid(), Tenant = Guid.NewGuid(), Batch = Guid.NewGuid(), Operation = Guid.NewGuid(), Invite = Guid.NewGuid();
        public WebApplication Host = null!; public HttpClient Http = null!; public int Commands, Reads;
        public string Path => $"/api/management/applications/{Application:D}/tenants/{Tenant:D}/invitation-revocations/{Batch:D}";
        private ManagementBulkInvitationRevokeResult Result(string outcome) => new(Tenant, Batch, Operation, outcome, "succeeded", 1, true,
            [new(Invite, ManagementBulkInvitationRevokeOperation.ItemOperationId(Operation, Invite), "revoked", null)]);
        public static async Task<Fixture> Create(bool install)
        {
            var f = new Fixture(); var service = new Mock<IManagementPortalBulkInvitationRevokeService>();
            service.Setup(x => x.ExecuteAsync(f.Application, f.Tenant, f.Batch, It.IsAny<ManagementBulkInvitationRevokeRequest>(), "bulk-route-intent", "tenant-version", It.IsAny<CancellationToken>()))
                .Callback(() => f.Commands++).ReturnsAsync(() => f.Result("accepted"));
            service.Setup(x => x.ReadReceiptAsync(f.Application, f.Tenant, f.Batch, f.Operation, It.IsAny<CancellationToken>())).Callback(() => f.Reads++).ReturnsAsync(() => f.Result("found"));
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["IBeam:Management:Api:AuthenticationScheme"] = "support" });
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, Auth>("support", _ => { }); builder.Services.AddSingleton(service.Object);
            if (install) builder.Services.AddIBeamManagementPortalBulkInvitationRevokeApi(builder.Configuration); else builder.Services.AddIBeamManagementApi(builder.Configuration);
            f.Host = builder.Build(); f.Host.UseAuthentication(); f.Host.UseAuthorization(); f.Host.MapControllers(); await f.Host.StartAsync(); f.Http = f.Host.GetTestClient(); return f;
        }
        public HttpRequestMessage Request(string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(new ManagementBulkInvitationRevokeRequest([new(Invite, "invite-version")])) };
            request.Headers.Authorization = new("Bearer", token); request.Headers.Add("Idempotency-Key", "bulk-route-intent"); request.Headers.TryAddWithoutValidation("If-Match", "tenant-version"); return request;
        }
        public async ValueTask DisposeAsync() { Http.Dispose(); await Host.DisposeAsync(); }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    { protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.Authorization == "Bearer support" ? AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "support")], Scheme.Name)), Scheme.Name)) : AuthenticateResult.Fail("Support authentication required.")); }
}
