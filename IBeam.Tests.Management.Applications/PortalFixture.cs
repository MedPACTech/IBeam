using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Models;
using IBeam.Management.Services;
using IBeam.Management.Services.Diagnostics;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.Applications;

internal sealed class PortalFixture : IDisposable
{
    public AccessFixture Access { get; } = new();
    public MemoryApplicationCredentials Credentials { get; } = new();
    public MemorySecrets Secrets { get; } = new();
    public PortalBackend Backend { get; } = new();
    public PortalClock Clock { get; } = new();
    public PortalDiagnosticsOptions Options { get; } = new() { ProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), TimeoutSeconds = 1 };
    public ManagementConnectionOptions NetworkOptions { get; } = new() { ApprovedOrigins = ["https://api.qurvia.app", "https://api.bindry.app"] };
    public ManagementConnectionNetworkPolicy Network { get; }
    public IManagementDiagnosticsService Service { get; }
    public PortalFixture()
    {
        Network = new(Microsoft.Extensions.Options.Options.Create(NetworkOptions), new ManagementTargetDnsResolver(), new ManagementTargetSocketConnector());
        var states = new ManagementApplicationStateProvider(Access.Apps);
        var resolver = new ManagementDiagnosticConnectionResolver(Access.Service, states, Credentials, Secrets, Access, Network);
        Service = new ManagementDiagnosticsService(Access.Service, resolver, Client(), states, new(Microsoft.Extensions.Options.Options.Create(Options)),
            Microsoft.Extensions.Options.Options.Create(Options), Clock, Access.Provider.GetRequiredService<IServiceOperationExecutor>());
    }
    public ApplicationDiagnosticsClient Client() => new(Network, Microsoft.Extensions.Options.Options.Create(Options)) { TestTransportFactory = () => new BackendHandler(Backend) };
    public async Task<Guid> AddApp(string key = "qurvia", params string[] grants)
    {
        Access.Actor = Access.Admin; Access.CanAdminister = true;
        var id = await Access.CreateApp(key);
        var credentialId = Guid.NewGuid(); var origin = "https://api." + key + ".app";
        Access.Apps.Rows[(Access.Org, id)] = Access.Apps.Rows[(Access.Org, id)] with { BaseUrl = origin, ActiveCredentialId = credentialId };
        var secret = ConnectionFixture.Key + "-" + key;
        var reference = await Secrets.CreateAsync(new(Access.Org, id), new(secret));
        await Credentials.CreateAsync(new() { OrganizationId = Access.Org, ApplicationId = id, CredentialId = credentialId,
            BaseUrl = origin, SecretReference = reference, Capabilities = [ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead] });
        Backend.Targets[new Uri(origin).Host] = (key, "Production", secret);
        await Access.Service.PutAsync(id, Access.Reader, new() { Permissions = grants.Length == 0 ? [ManagementOperations.ErrorsRead, ManagementOperations.LogsRead] : grants });
        Access.Actor = Access.Reader; Access.CanAdminister = false;
        return id;
    }
    public DiagnosticQuery Query(string? cursor = null) => new() { FromUtc = Clock.Now.AddHours(-2), ToUtc = Clock.Now, PageSize = 1, Cursor = cursor };
    public void Dispose() => Access.Dispose();
    internal sealed class BackendHandler(PortalBackend backend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => backend.Handle(request, ct);
    }
}
internal sealed class PortalClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
internal sealed class PortalBackend
{
    public Dictionary<string, (string Key, string Environment, string Secret)> Targets { get; } = [];
    public int Calls { get; set; }
    public int DataCalls { get; set; }
    public Func<CancellationToken, Task>? DuringInfo { get; set; }
    public HttpStatusCode? Status { get; set; }
    public bool WrongIdentity { get; set; }
    public bool CorruptBody { get; set; }
    public string[] UnsanitizedCapabilities { get; set; } = [];
    public bool IncludeRawBody { get; set; }
    public string RawMessage { get; set; } = ConnectionFixture.Key;
    public async Task<HttpResponseMessage> Handle(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        var target = Targets[request.RequestUri!.Host];
        Assert.AreEqual(target.Secret, request.Headers.GetValues("X-IBeam-Management-Key").Single());
        Assert.IsNull(request.Headers.Host); Assert.IsFalse(request.Headers.Contains("Authorization")); Assert.IsFalse(request.Headers.Contains("traceparent"));
        if (Status.HasValue) return new(Status.Value) { Content = new StringContent(ConnectionFixture.Key) };
        if (CorruptBody) return new(HttpStatusCode.OK) { Content = new StringContent(ConnectionFixture.Key) };
        if (request.RequestUri.AbsolutePath.EndsWith("/info", StringComparison.Ordinal))
        {
            if (DuringInfo is not null) await DuringInfo(ct);
            return Response(new ManagementDiagnosticsInfo(WrongIdentity ? "wrong" : target.Key, target.Environment, 1,
                [ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead]) { UnsanitizedCapabilities = UnsanitizedCapabilities.Length == 0 ? null : UnsanitizedCapabilities });
        }
        DataCalls++;
        var now = DateTimeOffset.UtcNow.AddMinutes(-30);
        var id = "d1_target" + target.Key;
        var log = request.RequestUri.AbsolutePath.Contains("system-logs", StringComparison.Ordinal);
        var detail = request.RequestUri.AbsolutePath.Count(x => x == '/') == 4;
        var errorRow = new SystemErrorDto(id, now, ConnectionFixture.Key, ConnectionFixture.Key, ConnectionFixture.Key) { Unsanitized = IncludeRawBody ? new(ConnectionFixture.Key, ConnectionFixture.Key, RawMessage, RawMessage, RawMessage, "POST") : null };
        var logRow = new SystemLogDto(id, now, "Error", ConnectionFixture.Key, ConnectionFixture.Key, ConnectionFixture.Key) { Unsanitized = IncludeRawBody ? new(ConnectionFixture.Key, "Error", ConnectionFixture.Key, RawMessage, RawMessage) : null };
        if (detail) return log ? Response(logRow) : Response(errorRow);
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = log ? (object)new[] { logRow } : new[] { errorRow },
            pageSize = 1, continuationToken = "d1_next" + target.Key }) };
    }
    private static HttpResponseMessage Response(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data }) };
}
