using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Operations;
using IBeam.Management.Services.Audit;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Management.Services;

public sealed class ManagementRegisteredOperationClientOptions
{
    public const string SectionName = "IBeam:Management:Operations:Portal";
    public string Issuer { get; set; } = "";
    public int LifetimeSeconds { get; set; } = 30;
    public string OperationIdKey { get; set; } = "";
    public List<ManagementActorTargetBinding> Targets { get; set; } = [];
    public override string ToString() => "Registered Management client options (keys redacted)";
}
/// <summary>Only typed registered contracts and live verified connections. No arbitrary URL/method/body proxy.</summary>
public sealed class ManagementRegisteredOperationClient(ManagementOperationRegistry registry, IManagementPrincipalProvider principals,
    IManagementApplicationAccessService access, IManagementRegisteredOperationAvailabilityService availability,
    IManagementApplicationStateProvider applications, IManagementApplicationCredentialRepository credentials, IManagementSecretStore secrets,
    ManagementCredentialSecretCodec keyCodec, IManagementActorSigningKeyProvider signing, IManagementAuditContext audit,
    ManagementConnectionNetworkPolicy network, IOptions<ManagementConnectionOptions> connectionOptions,
    IOptionsMonitor<ManagementRegisteredOperationClientOptions> options, TimeProvider clock, IServiceOperationExecutor operations) : IManagementTargetOperationSender
{
    internal Func<HttpMessageHandler>? TestTransportFactory { get; init; }
    public Task<TResult> ExecuteAsync<TRequest, TResult>(Guid applicationId, string operation, Guid tenantId, Guid resourceId,
        TRequest request, string idempotencyKey, string? expectedVersion, CancellationToken ct = default)
        => ManagementTargetTelemetry.Run("registered", "execute", ManagementTargetTelemetry.Registered(registry, operation), applicationId, tenantId, resourceId, audit, ct, async () => {
        var descriptor = (operation is null ? null : registry.Find(operation)) ?? throw new ManagementAccessDeniedException();
        if (descriptor.Request.ContractType != typeof(TRequest) || descriptor.Result.ContractType != typeof(TResult)) throw new ManagementOperationInvalidException();
        var body = new ManagementOperationPayloadCodec(registry).WriteRequest(operation, request);
        if (descriptor.Method == "GET" && (!string.IsNullOrEmpty(idempotencyKey) || expectedVersion is not null) ||
            descriptor.Method != "GET" && (!ManagementDelegationBinding.ValidIdempotencyKey(idempotencyKey) ||
                expectedVersion is not { Length: > 0 and <= 256 } || expectedVersion == "*" || expectedVersion.Any(char.IsControl))) throw new ManagementOperationInvalidException();
        return await Send<TResult>(applicationId, operation, tenantId, resourceId, Guid.Empty, body, idempotencyKey, expectedVersion, lookup: false, ct);
    });
    public Task<TResult> ReadReceiptAsync<TResult>(Guid applicationId, string operation, Guid tenantId, Guid resourceId, Guid operationId, CancellationToken ct = default)
        => ManagementTargetTelemetry.Run("registered", "receipt", ManagementTargetTelemetry.Registered(registry, operation), applicationId, tenantId, resourceId, audit, ct, async () => {
        var descriptor = (operation is null ? null : registry.Find(operation)) ?? throw new ManagementAccessDeniedException();
        if (descriptor.Result.ContractType != typeof(TResult) || descriptor.ReceiptProtocolVersion != 1 || operationId == Guid.Empty) throw new ManagementOperationInvalidException();
        ManagementTargetTelemetry.Operation(operationId);
        return await Send<TResult>(applicationId, operation, tenantId, resourceId, operationId, [], "", null, lookup: true, ct);
    });
    private Task<TResult> Send<TResult>(Guid appId, string operation, Guid tenant, Guid resource, Guid receiptId, byte[] body, string key,
        string? version, bool lookup, CancellationToken ct)
        => operations.ExecuteAsync(this, async token =>
        {
            var descriptor = registry.Find(operation) ?? throw new ManagementAccessDeniedException();
            if (appId == Guid.Empty || tenant == Guid.Empty || resource == Guid.Empty) throw new ManagementOperationInvalidException();
            var identity = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Human, ManagementOperations.IdentityRead);
            var org = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.OrganizationId);
            if (!Guid.TryParseExact(ManagementPrincipalGuard.RequireHumanUserId(identity), "D", out var human) || human == Guid.Empty) throw new ManagementAccessDeniedException();
            await Require(appId, operation, token);
            var app = await applications.GetAsync(org, appId, token);
            if (app is null || app.OrganizationId != org || app.ApplicationId != appId || !app.IsEnabled || app.ActiveCredentialId is not Guid connectionId)
                throw new ManagementAccessDeniedException();
            var connection = await credentials.GetAsync(org, appId, connectionId, token);
            if (connection is null || connection.OrganizationId != org || connection.ApplicationId != appId || connection.CredentialId != connectionId ||
                connection.BaseUrl != app.BaseUrl || connection.Operations is not { Status: "available", Info: { } info }) throw new ManagementAccessDeniedException();
            ManagementRegisteredOperationProtocol.Validate(info, registry);
            if (info.ApplicationKey != app.ApplicationKey || info.Environment != app.Environment) throw new ManagementAccessDeniedException();
            var configured = options.CurrentValue;
            var binding = configured.Targets.SingleOrDefault(x => x.ApplicationId == appId) ?? throw new ManagementAccessDeniedException();
            var secret = await secrets.ReadAsync(new(org, appId), connection.SecretReference, token);
            if (!keyCodec.TryReadId(secret.Value, out var credentialId)) throw new ManagementAccessDeniedException();
            var scope = new ManagementOperationContext(org, appId, info.RealmId, tenant, human, lookup ? receiptId : descriptor.Method == "GET" ? Guid.Empty :
                Intent(configured.OperationIdKey, org, appId, info.RealmId, human, tenant, resource, operation, key)) { ResourceId = resource, ResourceVersion = version };
            ManagementTargetTelemetry.Operation(scope.OperationId);
            var method = lookup ? "GET" : descriptor.Method;
            var path = lookup ? operation == ManagementTenantDataOperations.Export ? TenantData.ManagementTenantExportRoutes.StatusPath(tenant, receiptId) :
                "/api/management-operations/receipts/" + receiptId.ToString("D") : ManagementRegisteredRoute.Path(descriptor, tenant, resource);
            var sent = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(connectionOptions.Value.TimeoutSeconds));
                var origin = network.RequireOrigin(app.BaseUrl);
                using var client = ManagementTargetHttpTransport.CreateClient(network, origin, TestTransportFactory);
                // Refresh target metadata without actor authority; stale stored discovery never authorizes a new call.
                using (var probe = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "/api/management-operations/info")) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact })
                {
                    probe.Headers.Add("X-IBeam-Management-Key", secret.Value);
                    using var response = await client.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    var fresh = await ManagementTargetConnectionProbe.ReadRegisteredInfoResponseAsync(response, registry, timeout.Token);
                    if (fresh is not { Status: "available", Info: { } actual } || actual.ApplicationKey != info.ApplicationKey || actual.Environment != info.Environment ||
                        actual.RealmId != info.RealmId || registry.RequiredFor(operation).Any(x => registry.Find(x) is not null && !actual.Capabilities.Any(c => c.Operation == x)))
                        throw new ManagementOperationUnavailableException();
                }
                await Require(appId, operation, timeout.Token);
                var latest = await applications.GetAsync(org, appId, timeout.Token);
                if (latest is null || !latest.IsEnabled || latest.Version != app.Version || latest.ActiveCredentialId != connectionId) throw new ManagementAccessDeniedException();
                var wire = new ManagementDelegationRequest { Method = method, PathAndQuery = path, Body = body,
                    IdempotencyKey = method == "GET" ? null : key, IfMatch = version };
                var digest = lookup ? ManagementDelegationBinding.HashRegisteredReceipt(wire) : ManagementDelegationBinding.Hash(wire, operation, registry);
                using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(origin, path)) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
                request.Headers.Add("X-IBeam-Management-Key", secret.Value);
                request.Headers.Add("X-IBeam-Management-Actor", Assertion(configured, binding.Audience, scope, credentialId, info, operation, digest));
                if (method != "GET")
                {
                    request.Content = new ByteArrayContent(body); request.Content.Headers.ContentType = new("application/json");
                    request.Headers.Add("Idempotency-Key", key); request.Headers.TryAddWithoutValidation("If-Match", version);
                }
                sent = true;
                using var reply = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (reply.StatusCode == HttpStatusCode.Forbidden || reply.StatusCode == HttpStatusCode.Unauthorized) throw new ManagementAccessDeniedException();
                if (reply.StatusCode == HttpStatusCode.BadRequest) throw new ManagementOperationInvalidException();
                if (reply.StatusCode == HttpStatusCode.Conflict) throw new ManagementOperationConflictException();
                if (reply.StatusCode == HttpStatusCode.PreconditionFailed) throw new ManagementOperationPreconditionException();
                if (reply.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted)) throw new ManagementOperationUnavailableException();
                var responseBytes = await Read(reply, descriptor.MaximumResultBytes + 4096, timeout.Token);
                using var json = JsonDocument.Parse(responseBytes, new JsonDocumentOptions { MaxDepth = 10 });
                if (!json.RootElement.GetProperty("success").GetBoolean()) throw new ManagementOperationUnavailableException();
                var payload = Encoding.UTF8.GetBytes(json.RootElement.GetProperty("data").GetRawText());
                TResult result;
                try { result = new ManagementOperationPayloadCodec(registry).ReadResult<TResult>(operation, payload); }
                catch (ManagementAccessDeniedException) { throw new ManagementOperationUnavailableException(); }
                if (operation == ManagementTenantDataOperations.Export)
                {
                    var export = TenantData.ManagementTenantExportMetadata.Project((TenantData.ManagementTenantExportResult)(object)result!, tenant, scope.OperationId);
                    if (lookup && export.Outcome == "accepted" || !lookup && export.Outcome == "found") throw new ManagementOperationUnavailableException();
                    result = (TResult)(object)export;
                    if (export.Outcome == "unknown") ManagementTargetTelemetry.Unknown();
                }
                if (operation == ManagementTenantDataOperations.Delete)
                {
                    var purge = TenantData.ManagementTenantPurgeMetadata.Project((TenantData.ManagementTenantPurgeResult)(object)result!, tenant, scope.OperationId);
                    if (lookup && purge.Outcome is not ("found" or "unknown")) throw new ManagementOperationUnavailableException();
                    if (!lookup)
                    {
                        var command = new ManagementOperationPayloadCodec(registry).ReadRequest<TenantData.ManagementTenantPurgeRequest>(operation, body);
                        if (command.Stage == "plan" && (purge.Outcome is not ("planned" or "unknown") || purge.Plan is { } plan &&
                                (plan.PolicyKey != command.PolicyKey || plan.ExpectedVersion != command.ExpectedVersion)) ||
                            command.Stage == "confirm" && (purge.Outcome is not ("accepted" or "unknown") || purge.Job is { } job && job.PlanId != command.PlanId))
                            throw new ManagementOperationUnavailableException();
                    }
                    result = (TResult)(object)purge;
                    if (purge.Outcome == "unknown") ManagementTargetTelemetry.Unknown();
                }
                await Require(appId, operation, timeout.Token);
                latest = await applications.GetAsync(org, appId, timeout.Token);
                if (latest is null || !latest.IsEnabled || latest.Version != app.Version || latest.ActiveCredentialId != connectionId) throw new ManagementAccessDeniedException();
                return result;
            }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationInvalidException or ManagementOperationConflictException or ManagementOperationPreconditionException) &&
                (ex is not OperationCanceledException || !ct.IsCancellationRequested))
            {
                using var after = CancellationTokenSource.CreateLinkedTokenSource(ct); after.CancelAfter(TimeSpan.FromSeconds(5));
                await Require(appId, operation, after.Token);
                if (sent && !lookup && descriptor.Method != "GET") throw new ManagementOperationUncertainException(appId, tenant, resource, scope.OperationId);
                throw new ManagementOperationUnavailableException();
            }
        }, new ServiceOperationExecutionOptions { OperationName = "management.operations.send", AuditAction = registry.Find(operation)?.AuditAction ?? "management.operations.send",
            EntityId = appId, TenantId = tenant, EntityName = "ManagementRegisteredOperationClient", PermissionEnabled = false, AuditEnabled = true }, ct);
    private async Task Require(Guid app, string operation, CancellationToken ct)
    {
        foreach (var permission in registry.RequiredFor(operation)) await access.RequireAsync(app, permission, ct);
        var current = await availability.GetAsync(app, ct);
        if (current is not { Status: "available", Info: { } info } || registry.RequiredFor(operation).Any(x => registry.Find(x) is not null && !info.Capabilities.Any(c => c.Operation == x)))
            throw new ManagementAccessDeniedException();
        foreach (var permission in registry.RequiredFor(operation)) await access.RequireAsync(app, permission, ct);
    }
    private string Assertion(ManagementRegisteredOperationClientOptions configured, string audience, ManagementOperationContext scope, Guid credential,
        ManagementRegisteredOperationInfo info, string operation, string digest)
    {
        var key = signing.GetSigningCredentials();
        if (key.Algorithm != SecurityAlgorithms.RsaSha256 || !ManagementDelegationBinding.ValidKey(key.Key)) throw new ManagementAccessDeniedException();
        var now = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
        var token = new JwtSecurityToken(configured.Issuer, audience, [new("sub", scope.ActorUserId.ToString("D")), new("org", scope.OrganizationId.ToString("D")),
            new("app", scope.ApplicationId.ToString("D")), new("realm", scope.RealmId.ToString("D")), new("credential", credential.ToString("D")),
            new("application_key", info.ApplicationKey), new("environment", info.Environment), new("operation", operation), new("binding", digest),
            new("tenant", scope.TenantId.ToString("D")), new("resource", scope.ResourceId.ToString("D")), new("operation_id", scope.OperationId.ToString("D")),
            new("jti", Guid.NewGuid().ToString("D")), new("correlation", audit.CorrelationId),
            new("iat", now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)], now.UtcDateTime, now.AddSeconds(configured.LifetimeSeconds).UtcDateTime, key);
        token.Header["typ"] = ManagementRegisteredTargetAuthority.TokenType; return new JwtSecurityTokenHandler().WriteToken(token);
    }
    private static Guid Intent(string key, Guid org, Guid app, Guid realm, Guid human, Guid tenant, Guid resource, string operation, string idempotency)
        => new(HMACSHA256.HashData(Convert.FromBase64String(key), JsonSerializer.SerializeToUtf8Bytes(new { org, app, realm, human, tenant, resource, operation, idempotency })).AsSpan(0, 16));
    private static async Task<byte[]> Read(HttpResponseMessage response, int maximum, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > maximum) throw new ManagementOperationUnavailableException();
        using var input = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); var chunk = new byte[4096];
        while (true) { var count = await input.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, maximum + 1 - (int)output.Length)), ct);
            if (count == 0) break; output.Write(chunk, 0, count); if (output.Length > maximum) throw new ManagementOperationUnavailableException(); }
        return output.ToArray();
    }
}
