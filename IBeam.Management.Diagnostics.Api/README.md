# IBeam.Management.Diagnostics.Api

Optional target diagnostics API library for one application/environment. It remains independent of IBeam Identity and the separate portal host.

For persisted management credentials, supply the repository and register the opt-in adapter:
```csharp
builder.Services.AddIBeamManagementAzureTableCredentialStore(builder.Configuration);
builder.Services.AddIBeamManagementDiagnosticsCredentialApi(builder.Configuration);
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```
The first extension is in IBeam.Management.Repositories.AzureTable; the second is in IBeam.Management.Diagnostics.Api.Authentication. Supply a dedicated TableClient instead of configuration when using managed identity/table-scoped SAS. Runtime readers need read permission only; trusted tooling provisions the table and issues keys separately.

Configure IBeam:Management:Diagnostics:ApplicationKey and Environment with the exact local deployment identity. Credential storage uses independently configured IBeam:Management:Credentials:Storage:ConnectionString and TableName (default ManagementApiCredentials), never customer/Identity storage fallback. Keep credentials in a secret store. Nothing changes or renames IBeam.Admin.API.

The named IBeam.Management.Credential scheme accepts exactly one X-IBeam-Management-Key header, only on /api/management-diagnostics routes. It rejects Authorization plus key combinations and does not read keys from Bearer, query strings or bodies. Customer/default authentication settings stay unchanged, and the management handler refuses ordinary customer routes even when ASP.NET automatically selects a sole scheme as its default. Customer JWTs, portal human tokens and legacy tenant API keys cannot substitute.

Each request validates the key hash against its current record, expiry/status, explicit grants and exact app/environment. The principal contains credential identity, deployment, permissions and an opaque record version, with no secret/hash. IManagementDiagnosticsAccessService.RequireAsync repeats live record/version/grant checks before a query. Future error/log readers must invoke it inside their service operation before repository access. Rotation/revocation/permission changes invalidate stale principals; already authorized in-flight work may finish. Storage failures grant no access; configure the host's standard safe global error middleware for infrastructure errors.

GET /api/management-diagnostics/info requires management.diagnostics.info and returns application/environment, protocol version and only implemented capabilities. Only info is currently advertised. Errors/logs are later reader features; merely granting their permissions installs no endpoints. management.diagnostics.errors.read and management.diagnostics.logs.read are independent and have no wildcard or owner/admin bypass. Missing/invalid authentication returns 401; a valid credential lacking an operation returns safe 403.

Optional network configuration: IBeam:Management:Credentials:Network:AllowedNetworks is an array of up to 100 CIDRs, such as 10.20.0.0/16 or 2001:db8::/32. Empty means no additional IP restriction; configured ranges deny unknown addresses. IPv4-mapped addresses normalize to IPv4. The policy reads HttpContext.Connection.RemoteIpAddress and never consumes a raw forwarded header. A reverse-proxy host must explicitly configure trusted proxies/networks and run its forwarded-header middleware before authentication. This library does not enable arbitrary forwarded-header trust.

Operations execute through IServiceOperationExecutor. Credential ID, operation, success/failure and server correlation are available to the audit sink without key/hash snapshots. When no audit providers are already registered, this module supplies a safe HTTP actor/request context provider; existing host providers are retained and must supply equivalent safe context. Durable management audit is a later feature. Configure a durable sink/policy before production and redact X-IBeam-Management-Key from ingress/HTTP logging; never log requests or keys. No issue/rotate/revoke HTTP routes are exposed.

Bring-your-own authentication remains supported: register the host's trusted machine scheme and call AddIBeamManagementDiagnosticsApi(configuration, o => o.AuthenticationScheme = "YourManagementScheme") instead of the persisted-key adapter. That scheme must verify its own secret/status/deployment and issue explicit management context. The general API validates required scheme/deployment configuration and its services retain their original context guards. Do not register both API paths in one host.
