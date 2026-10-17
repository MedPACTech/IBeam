using IBeam.Management.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Management;
using IBeam.Management.Api;
using IBeam.Management.Entities;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.Services.Audit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;

var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE") ?? throw new InvalidOperationException("A private local Table fixture is required.");
var storage = new TableServiceClient(connection);
if (!IPAddress.TryParse(storage.Uri.Host, out var ip) || !IPAddress.IsLoopback(ip)) throw new InvalidOperationException("Smoke validation requires loopback-only storage.");
var prefix = "MgmtSmoke" + Guid.NewGuid().ToString("N");
var tables = new List<TableClient>();
async Task<TableClient> Table(string name)
{
    var table = storage.GetTableClient(prefix + name); await table.CreateIfNotExistsAsync(); tables.Add(table); return table;
}
try
{
    await using var qurvia = await SmokeTarget.Create("qurvia", await Table("QErrors"), await Table("QLogs"), await Table("QKeys"), await Table("QAudit"), rawErrors: true);
    await using var bindry = await SmokeTarget.Create("bindry", await Table("BErrors"), await Table("BLogs"), await Table("BKeys"), await Table("BAudit"), rawLogs: true);
    var state = new SmokeSupportState();
    var b = WebApplication.CreateBuilder(); b.Logging.ClearProviders(); b.WebHost.UseTestServer();
    b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
        ["IBeam:Management:PortalDiagnostics:ProtectionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        ["IBeam:Management:Connections:ApprovedOrigins:0"] = "https://api.qurvia.app", ["IBeam:Management:Connections:ApprovedOrigins:1"] = "https://api.bindry.app" });
    b.Services.AddSingleton(state);
    b.Services.AddSingleton<IManagementSupportUserStateProvider>(state);
    b.Services.AddAuthentication("SmokeSupport").AddScheme<AuthenticationSchemeOptions, SmokeAuthentication>("SmokeSupport", _ => {});
    b.Services.AddControllers().ConfigureApplicationPartManager(x => x.ApplicationParts.Clear());
    b.Services.AddIBeamManagementApi(b.Configuration, x => x.AuthenticationScheme = "SmokeSupport");
    b.Services.AddIBeamManagementConnectionsApi(b.Configuration);
    b.Services.AddIBeamManagementApplicationAccessApi();
    b.Services.AddIBeamManagementApplicationSelectorApi(b.Configuration);
    b.Services.AddIBeamManagementAzureTableApplicationStore(await Table("Applications"));
    b.Services.AddSingleton<IManagementApplicationCredentialRepository>(new AzureTableManagementApplicationCredentialRepository(await Table("Connections")));
    b.Services.AddSingleton<IManagementApplicationAccessRepository>(new AzureTableManagementApplicationAccessRepository(await Table("Assignments")));
    b.Services.AddIBeamManagementAzureTableAuditStore(await Table("PortalAudit")); b.Services.AddIBeamManagementAudit(b.Configuration);
    b.Services.AddSingleton<IManagementSecretStore, SmokeSecrets>();
    var targets = new Dictionary<string, SmokeTarget> { ["https://api.qurvia.app"] = qurvia, ["https://api.bindry.app"] = bindry };
    b.Services.AddScoped<IApplicationDiagnosticsClient>(p => new InProcessDiagnosticsClient(targets, p.GetRequiredService<IManagementAuditContext>()));
    b.Services.AddScoped<IManagementTargetConnectionProbe>(p => new SmokeProbe(p.GetRequiredService<IApplicationDiagnosticsClient>()));
    await using var portal = b.Build(); portal.UseAuthentication(); portal.UseAuthorization(); portal.MapControllers(); await portal.StartAsync();
    using var client = portal.GetTestClient();
    async Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpMethod method, string path, string token = "operator", object? body = null, string? version = null)
    {
        using var request = new HttpRequestMessage(method, path); request.Headers.Add("Authorization", "Bearer " + token);
        if (body is not null) request.Content = JsonContent.Create(body); if (version is not null) request.Headers.Add("If-Match", version);
        using var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        Check(!text.Contains(qurvia.Key, StringComparison.Ordinal) && !text.Contains(bindry.Key, StringComparison.Ordinal), "Public response leaked a credential.");
        if (text.Length > 0) { using var parsed = JsonDocument.Parse(text); CheckProjection(parsed.RootElement); }
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }
    var apps = new List<(Guid Id, string GrantVersion)>();
    foreach (var target in new[] { qurvia, bindry })
    {
        using var info = await target.Get("info", target.Key); Check(info.StatusCode == HttpStatusCode.OK, "Authenticated target info failed.");
        using var declared = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        var rawModes = declared.RootElement.GetProperty("data").GetProperty("unsanitizedCapabilities");
        Check(rawModes.GetArrayLength() == 1 && rawModes[0].GetString() == (target.Application == "qurvia" ? ManagementOperations.ErrorsRead : ManagementOperations.LogsRead), "Target mode advertisement is incorrect.");
        using var wrongKey = await target.Get("info", target == qurvia ? bindry.Key : qurvia.Key); Check(wrongKey.StatusCode == HttpStatusCode.Unauthorized, "Target keys were interchangeable.");
        var created = await Send(HttpMethod.Post, "/api/management/applications", body: new { applicationKey = target.Application, name = target.Application, environment = target.Environment, baseUrl = "https://api." + target.Application + ".app" });
        Check(created.Status == HttpStatusCode.Created, "Application registration failed."); var id = created.Body.GetProperty("data").GetProperty("applicationId").GetGuid();
        var version = created.Body.GetProperty("data").GetProperty("version").GetString();
        var connected = await Send(HttpMethod.Put, $"/api/management/applications/{id}/connection", body: new { apiKey = target.Key, expectedVersion = version });
        Check(connected.Status == HttpStatusCode.OK, "Verified target connection failed.");
        var grant = await Send(HttpMethod.Put, $"/api/management/applications/{id}/support-users/{state.Reader}", body: new { permissions = new[] { ManagementOperations.ErrorsRead, ManagementOperations.LogsRead } });
        Check(grant.Status == HttpStatusCode.OK, "Assignment failed."); apps.Add((id, grant.Body.GetProperty("data").GetProperty("version").GetString()!));
    }
    var selected = await Send(HttpMethod.Get, "/api/management/applications", "support"); Check(selected.Status == HttpStatusCode.OK && selected.Body.GetProperty("data").GetArrayLength() == 2, "Multi-app selector failed.");
    var ids = new List<string>(); var cursors = new List<string>();
    foreach (var app in apps)
    foreach (var kind in new[] { "system-errors", "system-logs" })
    {
        var path = $"/api/management/applications/{app.Id}/{kind}";
        var page = await Send(HttpMethod.Get, path + "?pageSize=1", "support");
        Check(page.Status == HttpStatusCode.OK && page.Body.GetProperty("data").GetArrayLength() == 1, "Safe diagnostic page failed.");
        var item = page.Body.GetProperty("data")[0];
        var expectedRaw = app.Id == apps[0].Id ? kind == "system-errors" : kind == "system-logs";
        Check(item.TryGetProperty("unsanitized", out var raw) == expectedRaw, "Independent target modes were not preserved by the portal.");
        if (expectedRaw) Check(raw.GetProperty("message").GetString() == SmokeTarget.Canary, "Explicit raw fields missing.");
        var id = item.GetProperty("id").GetString()!;
        var cursor = page.Body.GetProperty("continuationToken").GetString()!; Check(cursor is not null, "Diagnostic continuation missing.");
        var detail = await Send(HttpMethod.Get, path + "/" + id, "support"); Check(detail.Status == HttpStatusCode.OK, "Safe detail failed.");
        var next = await Send(HttpMethod.Get, path + "?continuationToken=" + cursor, "support"); Check(next.Status == HttpStatusCode.OK && next.Body.GetProperty("data").GetArrayLength() == 1, "Continuation failed.");
        if (kind == "system-errors") { ids.Add(id); cursors.Add(cursor!); }
    }
    var cross = await Send(HttpMethod.Get, $"/api/management/applications/{apps[1].Id}/system-errors/{ids[0]}", "support"); Check(cross.Status == HttpStatusCode.BadRequest, "Cross-app ID replay succeeded.");
    var tamper = await Send(HttpMethod.Get, $"/api/management/applications/{apps[0].Id}/system-errors?continuationToken={cursors[0]}x", "support"); Check(tamper.Status == HttpStatusCode.BadRequest, "Tampered cursor succeeded.");
    var foreign = await Send(HttpMethod.Get, $"/api/management/applications/{apps[0].Id}/system-errors", "foreign"); Check(foreign.Status == HttpStatusCode.Forbidden, "Cross-organization read succeeded.");
    var customer = await Send(HttpMethod.Get, "/api/management/applications", "customer"); Check(customer.Status == HttpStatusCode.Unauthorized, "Customer substitution succeeded.");
    var revoke = await Send(HttpMethod.Delete, $"/api/management/applications/{apps[0].Id}/support-users/{state.Reader}", version: apps[0].GrantVersion); Check(revoke.Status == HttpStatusCode.NoContent, "Revocation failed.");
    Check((await Send(HttpMethod.Get, $"/api/management/applications/{apps[0].Id}/system-errors", "support")).Status == HttpStatusCode.Forbidden, "Revoked read succeeded.");
    Check((await Send(HttpMethod.Get, "/api/management/applications", "support")).Body.GetProperty("data").GetArrayLength() == 1, "Revoked selector entry remained.");
    var portalAudit = new List<TableEntity>(); await foreach (var row in tables.Single(x => x.Name.EndsWith("PortalAudit", StringComparison.Ordinal)).QueryAsync<TableEntity>()) portalAudit.Add(row);
    var targetAudit = new List<TableEntity>(); foreach (var target in new[] { qurvia, bindry }) await foreach (var row in target.Audit.QueryAsync<TableEntity>()) targetAudit.Add(row);
    Check(portalAudit.Any(x => x.GetString("Operation") == "management.portal.errors.list" && targetAudit.Any(y => y.GetString("Operation") == ManagementOperations.ErrorsRead && y.GetString("CorrelationId") == x.GetString("CorrelationId"))), "Portal/target audit correlation missing.");
    Check(!JsonSerializer.Serialize(portalAudit.Concat(targetAudit)).Contains(SmokeTarget.Canary, StringComparison.Ordinal), "Audit leaked a payload.");
    Console.WriteLine("PASS: package-only portal + two isolated targets; info, onboarding, assignments, errors/logs pages/details, app/user isolation, revocation and correlated safe audit.");
}
finally { foreach (var table in tables) await table.DeleteAsync(); }
static void Check(bool condition, string failure) { if (!condition) throw new InvalidOperationException(failure); }

static void CheckProjection(JsonElement value)
{
    if (value.ValueKind == JsonValueKind.Object)
        foreach(var property in value.EnumerateObject())
        {
            if (property.Name == "unsanitized")
            {
                Check(property.Value.ValueKind == JsonValueKind.Object, "Raw marker must be an object.");
                foreach(var field in property.Value.EnumerateObject())
                    Check(new[] { "source", "level", "traceId", "message", "exception", "path", "method", "detail" }.Contains(field.Name) &&
                        field.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null, "Unexpected raw field exposed.");
            }
            else CheckProjection(property.Value);
        }
    else if (value.ValueKind == JsonValueKind.Array) foreach(var item in value.EnumerateArray()) CheckProjection(item);
    else if (value.ValueKind == JsonValueKind.String) Check(!value.GetString()!.Contains(SmokeTarget.Canary, StringComparison.Ordinal), "Raw text escaped its explicit marker.");
}