using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using IBeam.Api.Middleware;
using IBeam.Api.Models;
using IBeam.Management;
using IBeam.Management.Diagnostics.Api.Authentication;
using IBeam.Management.Diagnostics.Api.Controllers;
using IBeam.Management.Entities;
using IBeam.Management.Models;
using IBeam.Management.Services.Credentials;
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

namespace IBeam.Tests.Management.Diagnostics;

internal sealed class SystemErrorApiFixture : IAsyncDisposable
{
    public const string Sensitive = "fixture-private-payload-password";
    public readonly ErrorReaderFixture.MemoryRepository Rows = new();
    public readonly LogReaderFixture.MemoryRepository LogRows = new();
    public readonly ErrorReaderFixture.Clock Clock = new();
    public readonly CredentialStore Credentials = new();
    public readonly AuditSink Audits = new();
    public readonly ServiceLogSink Logs = new();
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public bool SimulateStorageFailure { get; set; }

    public static async Task<SystemErrorApiFixture> CreateAsync(
        bool installed = true, ISystemErrorReadRepository? repository = null,
        Action<Dictionary<string, string?>>? configure = null, bool featureFirst = false, bool registerRepository = true,
        bool installLogs = false, ISystemLogReadRepository? logRepository = null)
    {
        var fixture = new SystemErrorApiFixture();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(fixture.Logs);
        builder.WebHost.UseTestServer();
        var values = new Dictionary<string, string?>
        {
            ["IBeam:Management:Diagnostics:ApplicationKey"] = "qurvia",
            ["IBeam:Management:Diagnostics:Environment"] = "Production",
            ["IBeam:Management:Diagnostics:Errors:SourceMode"] = "DedicatedDeployment",
            ["IBeam:Management:Diagnostics:Errors:DedicatedSourceConfirmed"] = "true",
            ["IBeam:Management:Diagnostics:Errors:ProtectionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["IBeam:Management:Diagnostics:Logs:SourceMode"] = "DedicatedDeployment",
            ["IBeam:Management:Diagnostics:Logs:DedicatedSourceConfirmed"] = "true",
            ["IBeam:Management:Diagnostics:Logs:ProtectionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };
        configure?.Invoke(values);
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddSingleton<TimeProvider>(fixture.Clock);
        builder.Services.AddSingleton<IManagementApiCredentialRepository>(fixture.Credentials);
        builder.Services.AddSingleton<IAuditTrailSink>(fixture.Audits);
        builder.Services.Configure<ServiceAuditOptions>(o => o.Enabled = true);
        if (registerRepository) builder.Services.AddSingleton<ISystemErrorReadRepository>(new FailureBoundary(fixture, repository ?? fixture.Rows));
        if (registerRepository) builder.Services.AddSingleton<ISystemLogReadRepository>(new LogFailureBoundary(fixture, logRepository ?? fixture.LogRows));
        builder.Services.AddControllers().ConfigureApplicationPartManager(p => p.ApplicationParts.Clear());
        builder.Services.AddAuthentication("Customer")
            .AddScheme<AuthenticationSchemeOptions, CustomerHandler>("Customer", _ => { });
        if (installed && featureFirst) builder.Services.AddIBeamManagementSystemErrorsApi(builder.Configuration);
        builder.Services.AddIBeamManagementDiagnosticsCredentialApi(builder.Configuration);
        if (installed && !featureFirst) builder.Services.AddIBeamManagementSystemErrorsApi(builder.Configuration);
        if (installLogs) builder.Services.AddIBeamManagementSystemLogsApi(builder.Configuration);
        // Even a detailed host error policy must not leak unsafe provider text from these endpoints.
        builder.Services.Configure<ApiErrorHandlingOptions>(o => o.ExposeDetailedErrors = true);
        fixture.App = builder.Build();
        fixture.App.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.TraceIdentifier = "errors-fixture:" + Guid.NewGuid().ToString("N");
            await next();
        });
        fixture.App.UseMiddleware<ApiExceptionMiddleware>();
        fixture.App.UseAuthentication();
        fixture.App.UseAuthorization();
        fixture.App.MapControllers();
        fixture.App.MapGet("/customer-only", () => Results.Ok("customer")).RequireAuthorization();
        try
        {
            await fixture.App.StartAsync();
            fixture.Client = fixture.App.GetTestClient();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task<ManagementIssuedCredential> IssueAsync(params string[] capabilities)
    {
        var id = Guid.NewGuid();
        var codec = new ManagementCredentialSecretCodec();
        var key = codec.Create(id);
        var row = await Credentials.CreateAsync(new ManagementApiCredentialEntity
        {
            CredentialId = id, ApplicationKey = "qurvia", Environment = "Production",
            SecretHash = codec.Hash(key), Capabilities = capabilities.Length == 0 ? [ManagementOperations.ErrorsRead] : capabilities,
            CreatedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        return new ManagementIssuedCredential { ApiKey = key, Credential = new ManagementCredentialMetadata(
            id, row.ApplicationKey, row.Environment, row.Capabilities, row.Status, row.ExpiresUtc) };
    }
    public Task<HttpResponseMessage> SendAsync(string path, string? key = null, HttpMethod? method = null, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (key is not null) request.Headers.Add(ManagementCredentialAuthenticationDefaults.HeaderName, key);
        return SendAndDisposeAsync(request, ct);
    }
    private async Task<HttpResponseMessage> SendAndDisposeAsync(HttpRequestMessage request, CancellationToken ct)
    { using(request) return await Client.SendAsync(request, ct); }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null) await App.DisposeAsync();
    }
    private sealed class FailureBoundary(SystemErrorApiFixture fixture, ISystemErrorReadRepository inner) : ISystemErrorReadRepository
    {
        public string SourceBinding => inner.SourceBinding;
        public Task<SystemErrorPartitionPage> ReadPartitionAsync(SystemErrorPartitionQuery query, CancellationToken ct = default)
            => fixture.SimulateStorageFailure ? throw new InvalidOperationException("provider signed-url password=" + Sensitive) : inner.ReadPartitionAsync(query, ct);
        public Task<SystemErrorRecord?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
            => fixture.SimulateStorageFailure ? throw new InvalidOperationException("provider connection password=" + Sensitive) : inner.GetAsync(partitionKey, rowKey, ct);
    }
    private sealed class LogFailureBoundary(SystemErrorApiFixture fixture, ISystemLogReadRepository inner) : ISystemLogReadRepository
    {
        public string SourceBinding => inner.SourceBinding;
        public Task<SystemLogPartitionPage> ReadPartitionAsync(SystemLogPartitionQuery query, CancellationToken ct = default)
            => fixture.SimulateStorageFailure ? throw new InvalidOperationException("provider signed-url password=" + Sensitive) : inner.ReadPartitionAsync(query, ct);
        public Task<SystemLogMetadata?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
            => fixture.SimulateStorageFailure ? throw new InvalidOperationException("provider connection password=" + Sensitive) : inner.GetAsync(partitionKey, rowKey, ct);
    }
    internal sealed class CredentialStore : IManagementApiCredentialRepository
    {
        private readonly ConcurrentDictionary<Guid, ManagementApiCredentialEntity> _rows = new();
        public Task<ManagementApiCredentialEntity?> GetAsync(string applicationKey, string environment, Guid credentialId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var row = _rows.GetValueOrDefault(credentialId);
            return Task.FromResult(row?.ApplicationKey == applicationKey && row.Environment == environment ? row : null);
        }
        public Task<ManagementApiCredentialEntity> CreateAsync(ManagementApiCredentialEntity credential, CancellationToken ct = default)
        {
            var row = credential with { Version = Guid.NewGuid().ToString("N") };
            if (!_rows.TryAdd(row.CredentialId, row)) throw new ManagementCredentialConflictException();
            return Task.FromResult(row);
        }
        public Task<ManagementApiCredentialEntity> ReplaceAsync(ManagementApiCredentialEntity credential, string expectedVersion, CancellationToken ct = default)
        {
            var old = _rows.GetValueOrDefault(credential.CredentialId);
            var updated = credential with { Version = Guid.NewGuid().ToString("N") };
            if (old is null || old.Version != expectedVersion || !_rows.TryUpdate(old.CredentialId, updated, old)) throw new ManagementCredentialConflictException();
            return Task.FromResult(updated);
        }
    }
    internal sealed class AuditSink : IAuditTrailSink
    {
        public ConcurrentQueue<ServiceAuditTransaction> Transactions { get; } = new();
        public Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default)
        { Transactions.Enqueue(transaction); return Task.CompletedTask; }
        public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default) => Task.CompletedTask;
    }
    internal sealed class ServiceLogSink : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);
        public void Dispose() { }
        private sealed class Recorder(ServiceLogSink sink, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => category.StartsWith("IBeam.Management", StringComparison.Ordinal);
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(level)) sink.Messages.Enqueue(formatter(state, exception) + exception);
            }
        }
    }
    private sealed class CustomerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization != "Bearer customer") return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("sub", "customer-user")], Scheme.Name)), Scheme.Name)));
        }
    }
}
