using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Azure.Data.Tables;
using Azure.Data.Tables.Sas;
using IBeam.Management;
using IBeam.Management.Diagnostics.Api.Authentication;
using IBeam.Management.Diagnostics.Api.Controllers;
using IBeam.Management.Entities;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.Repositories.AzureTable.Diagnostics;
using IBeam.Management.Services.Audit;
using IBeam.Management.Services.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

internal sealed class SmokeTarget(WebApplication app, string application, string key, TableClient audit) : IAsyncDisposable
{
    public const string Canary = "package-smoke-private-client-payload";
    public string Application { get; } = application;
    public string Key { get; } = key;
    public string Environment { get; } = "Testing";
    public TableClient Audit { get; } = audit;
    public HttpClient Client { get; } = app.GetTestClient();
    public static async Task<SmokeTarget> Create(string application, TableClient errors, TableClient logs, TableClient credentials, TableClient audit, bool rawErrors = false, bool rawLogs = false)
    {
        for (var i = 0; i < 2; i++)
        {
            var at = DateTimeOffset.UtcNow.AddMinutes(-30 + i);
            var error = new TableEntity("ERR|" + at.ToString("yyyyMMdd"), at.ToString("HHmmssfff") + "|" + Guid.NewGuid().ToString("N")) {
                ["OccurredAtUtc"] = at, ["Source"] = "RequestPipeline", ["TraceId"] = Canary, ["Message"] = Canary, ["Exception"] = Canary, ["Path"] = Canary };
            await errors.AddEntityAsync(error);
            var log = new TableEntity("SYSTEM|DAY|" + at.ToString("yyyyMMdd"), at.ToString("HHmmssfffffff") + "|" + Guid.NewGuid().ToString("N")) {
                ["OccurredAtUtc"] = at, ["Category"] = "System", ["IsSelectRollup"] = false, ["Source"] = "RequestPipeline", ["TraceId"] = Canary,
                ["Level"] = "Error", ["Message"] = Canary, ["Detail"] = Canary, ["OriginalJson"] = Canary, ["TransformedJson"] = Canary };
            await logs.AddEntityAsync(log);
        }
        var id = Guid.NewGuid(); var codec = new ManagementCredentialSecretCodec(); var key = codec.Create(id);
        await new AzureTableManagementApiCredentialRepository(credentials).CreateAsync(new ManagementApiCredentialEntity {
            CredentialId = id, ApplicationKey = application, Environment = "Testing", SecretHash = codec.Hash(key), CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Capabilities = [ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead] });
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); b.Logging.ClearProviders(); b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["IBeam:Management:Diagnostics:ApplicationKey"] = application, ["IBeam:Management:Diagnostics:Environment"] = "Testing",
            ["IBeam:Management:Diagnostics:Errors:Sanitization:Enabled"] = rawErrors ? "false" : "true",
            ["IBeam:Management:Diagnostics:Logs:Sanitization:Enabled"] = rawLogs ? "false" : "true",
            ["IBeam:Management:Diagnostics:Errors:SourceMode"] = "DedicatedDeployment", ["IBeam:Management:Diagnostics:Errors:DedicatedSourceConfirmed"] = "true",
            ["IBeam:Management:Diagnostics:Errors:ProtectionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["IBeam:Management:Diagnostics:Logs:SourceMode"] = "DedicatedDeployment", ["IBeam:Management:Diagnostics:Logs:DedicatedSourceConfirmed"] = "true",
            ["IBeam:Management:Diagnostics:Logs:ProtectionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        b.Services.AddControllers().ConfigureApplicationPartManager(x => x.ApplicationParts.Clear());
        b.Services.AddIBeamManagementAzureTableCredentialStore(credentials);
        b.Services.AddIBeamManagementAzureTableSystemErrorReader(new TableClient(errors.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(20))));
        b.Services.AddIBeamManagementAzureTableSystemLogReader(new TableClient(logs.GenerateSasUri(TableSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(20))));
        b.Services.AddIBeamManagementAzureTableAuditStore(audit); b.Services.AddIBeamManagementAudit(b.Configuration);
        b.Services.AddIBeamManagementDiagnosticsCredentialApi(b.Configuration);
        b.Services.AddIBeamManagementSystemErrorsApi(b.Configuration); b.Services.AddIBeamManagementSystemLogsApi(b.Configuration);
        var app = b.Build(); app.Use(async (context, next) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; await next(); });
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync(); return new(app, application, key, audit);
    }
    public async Task<HttpResponseMessage> Get(string path, string key)
    { using var request = new HttpRequestMessage(HttpMethod.Get, "/api/management-diagnostics/" + path); request.Headers.Add("X-IBeam-Management-Key", key); return await Client.SendAsync(request); }
    public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
}
internal sealed class SmokeSupportState : IManagementSupportUserStateProvider
{
    public Guid Organization { get; } = Guid.NewGuid();
    public Guid ForeignOrganization { get; } = Guid.NewGuid();
    public Guid Administrator { get; } = Guid.NewGuid();
    public Guid Reader { get; } = Guid.NewGuid();
    public Task<ManagementSupportUserState?> GetAsync(Guid organizationId, Guid userId, CancellationToken ct = default)
        => Task.FromResult(organizationId == Organization && (userId == Administrator || userId == Reader) ? new ManagementSupportUserState(organizationId, userId, true, userId == Administrator) : null);
}
// Fixture-only authentication. It never opens a network listener and must not be deployed.
internal sealed class SmokeAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, SmokeSupportState state)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers.Authorization.ToString();
        if (token is not ("Bearer operator" or "Bearer support" or "Bearer foreign")) return Task.FromResult(AuthenticateResult.NoResult());
        var admin = token == "Bearer operator";
        var claims = new List<Claim> { new("sub", (admin ? state.Administrator : state.Reader).ToString()), new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human),
            new(ManagementClaimTypes.OrganizationId, (token == "Bearer foreign" ? state.ForeignOrganization : state.Organization).ToString()), new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead) };
        if (admin) foreach (var permission in new[] { ManagementApplicationOperations.Admin, ManagementSupportOperations.AccessAdmin, ManagementAccessDelegation.Errors, ManagementAccessDelegation.Logs }) claims.Add(new(ManagementClaimTypes.Permission, permission));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}
internal sealed class SmokeSecrets : IManagementSecretStore
{
    private readonly Dictionary<(ManagementSecretScope, string), ManagementSecretValue> _values = [];
    public bool SupportsWrites => true;
    public Task<string> CreateAsync(ManagementSecretScope scope, ManagementSecretValue value, CancellationToken ct = default)
    { var id = Guid.NewGuid().ToString("N"); _values[(scope, id)] = value; return Task.FromResult(id); }
    public Task<ManagementSecretValue> ReadAsync(ManagementSecretScope scope, string reference, CancellationToken ct = default) => Task.FromResult(_values[(scope, reference)]);
}
