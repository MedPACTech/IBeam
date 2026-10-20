using IBeam.Management.Services.Audit;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IBeam.Management;
using IBeam.Management.Api;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.Applications;

internal sealed class PortalHttpFixture : IAsyncDisposable
{
    public PortalFixture Data { get; } = new();
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public MemoryManagementAudits Audit { get; } = new();
    public Guid OtherUser { get; } = Guid.NewGuid();
    public Guid OtherOrg { get; } = Guid.NewGuid();
    public Guid ForeignUser { get; } = Guid.NewGuid();
    public CustomerDirectoryBackend Directories { get; } = new();
    public static async Task<PortalHttpFixture> CreateAsync(bool install = true, bool selector = false, bool audit = false, bool customerIdentity = false, bool customerDirectories = false, bool customerOperations = false, bool customerMutations = false, bool customerOnboarding = false)
    {
        var f = new PortalHttpFixture();
        f.Data.Access.Users[(f.Data.Access.Org, f.OtherUser)] = new(f.Data.Access.Org, f.OtherUser, true, false);
        f.Data.Access.Users[(f.OtherOrg, f.ForeignUser)] = new(f.OtherOrg, f.ForeignUser, true, false);
        var b = WebApplication.CreateBuilder(); b.Logging.ClearProviders(); b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["IBeam:Management:Connections:ApprovedOrigins:0"] = "https://api.qurvia.app",
            ["IBeam:Management:Connections:ApprovedOrigins:1"] = "https://api.bindry.app",
            ["IBeam:Management:Connections:ApprovedOrigins:2"] = "https://api.medinsights.app",
            ["IBeam:Management:PortalDiagnostics:ProtectionKey"] = f.Data.Options.ProtectionKey,
            ["IBeam:Management:PortalDiagnostics:TimeoutSeconds"] = "1",
            ["IBeam:Management:PortalCustomerIdentity:ProtectionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            ["IBeam:Management:PortalCustomerIdentity:TimeoutSeconds"] = "1" });
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["IBeam:Management:PortalCustomerIdentity:Mutations:AttemptTimeoutSeconds"] = "1",
            ["IBeam:Management:PortalCustomerIdentity:Mutations:AuthorizationTimeoutSeconds"] = "1" });
        b.Services.AddSingleton(f);
        b.Services.AddAuthentication("Customer").AddScheme<AuthenticationSchemeOptions, PortalAuthentication>("Customer", _ => {})
            .AddScheme<AuthenticationSchemeOptions, PortalAuthentication>("Support", _ => {});
        b.Services.AddControllers().ConfigureApplicationPartManager(x => x.ApplicationParts.Clear());
        b.Services.AddIBeamManagementApi(b.Configuration, x => x.AuthenticationScheme = "Support");
        b.Services.AddSingleton<IManagementApplicationRepository>(f.Data.Access.Apps);
        b.Services.AddSingleton<IManagementApplicationAccessRepository>(f.Data.Access.Access);
        b.Services.AddSingleton<IManagementSupportUserStateProvider>(f.Data.Access);
        b.Services.AddSingleton<IManagementApplicationCredentialRepository>(f.Data.Credentials);
        b.Services.AddSingleton<IManagementSecretStore>(f.Data.Secrets);
        b.Services.AddSingleton<IApplicationDiagnosticsClient>(_ => f.Data.Client());
        b.Services.AddSingleton<TimeProvider>(f.Data.Clock);
        if (install) b.Services.AddIBeamManagementPortalDiagnosticsApi(b.Configuration);
        if (selector) b.Services.AddIBeamManagementApplicationSelectorApi(b.Configuration);
        if (customerIdentity) b.Services.AddIBeamManagementCustomerIdentityApi(b.Configuration);
        if (customerDirectories || customerOperations || customerMutations || customerOnboarding) {
            b.Services.AddSingleton<IBeam.Management.CustomerIdentity.IManagementDelegatedActorProvider>(f.Directories);
            b.Services.AddSingleton<IBeam.Management.CustomerIdentity.IManagementCustomerIdentityClient>(provider =>
                new IBeam.Management.Services.CustomerIdentity.ManagementCustomerIdentityClient(f.Data.Network,
                    provider.GetRequiredService<IOptions<IBeam.Management.Services.CustomerIdentity.ManagementPortalCustomerDirectoryOptions>>(), f.Directories)
                    { TestTransportFactory = () => new CustomerDirectoryBackend.Handler(f.Directories) });
            if (customerDirectories) b.Services.AddIBeamManagementPortalCustomerDirectoriesApi(b.Configuration);
            if (customerOperations) b.Services.AddIBeamManagementPortalIdentityOperationsApi(b.Configuration);
            if (customerMutations) { f.Directories.EnableMutations = true; b.Services.AddIBeamManagementPortalTenantAdministrationApi(b.Configuration); }
            if (customerOnboarding) { f.Directories.EnableOnboarding = true; b.Services.AddIBeamManagementPortalEnterpriseOnboardingApi(b.Configuration); }
        }
        if (audit) { b.Services.AddSingleton<IManagementAuditRepository>(f.Audit); b.Services.AddIBeamManagementAudit(b.Configuration); }
        f.App = b.Build(); f.App.UseAuthentication(); f.App.UseAuthorization(); f.App.MapControllers();
        try { await f.App.StartAsync(); } catch { await f.App.DisposeAsync(); f.Data.Dispose(); throw; }
        f.Client = f.App.GetTestClient(); return f;
    }
    public async Task<HttpResponseMessage> Get(string path, string token = "reader")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Authorization", "Bearer " + token); return await Client.SendAsync(request);
    }
    public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); Data.Dispose(); }
}
internal sealed class PortalAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, PortalHttpFixture fixture) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
        if (Scheme.Name != "Support" || token is not ("reader" or "other" or "foreign" or "admin" or "machine")) return Task.FromResult(AuthenticateResult.NoResult());
        var org = token == "foreign" ? fixture.OtherOrg : fixture.Data.Access.Org;
        var user = token switch { "other" => fixture.OtherUser, "foreign" => fixture.ForeignUser, "admin" => fixture.Data.Access.Admin, _ => fixture.Data.Access.Reader };
        var claims = new List<Claim> { new("sub", user.ToString()), new(ManagementClaimTypes.SubjectType, token == "machine" ? ManagementSubjectTypes.Credential : ManagementSubjectTypes.Human),
            new(ManagementClaimTypes.OrganizationId, org.ToString()), new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead) };
        if (token == "admin") claims.Add(new(ManagementClaimTypes.Permission, ManagementApplicationOperations.Admin));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
