using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;
internal sealed partial class CustomerDirectoryBackend
{
    public bool EnableTenantStatus { get; set; }
    public int TenantStatusWrites { get; private set; }
    private readonly Dictionary<string, (bool Active, string Version)> _statusTenants = [];
    private readonly Dictionary<(string Host, string Key), (string Fingerprint, ManagementIdentityOperationDto Receipt)> _statusOperations = [];
    private async Task<HttpResponseMessage> TenantStatus(HttpRequestMessage request, string authority, CancellationToken ct)
    {
        Assert.AreEqual(ManagementCustomerIdentityOperations.TenantsStatus, authority); var uri = request.RequestUri!; var host = uri.Host;
        if (request.Method == HttpMethod.Get) {
            var row = _statusOperations.Where(x => x.Key.Host == host).Select(x => x.Value.Receipt).SingleOrDefault(x => uri.AbsolutePath.EndsWith(x.OperationId.ToString("D"), StringComparison.Ordinal));
            return row is null ? new(HttpStatusCode.NotFound) : Response(row);
        }
        var bytes = await request.Content!.ReadAsByteArrayAsync(ct); var key = request.Headers.GetValues("Idempotency-Key").Single();
        if (uri.AbsolutePath.EndsWith("/operations/resolve", StringComparison.Ordinal)) {
            LookupCalls++; var lookup = JsonSerializer.Deserialize<ManagementIdentityOperationLookup>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (lookup.Operation != authority || lookup.ResourceScope != Tenant || lookup.ExistingResourceId != Tenant) return new(HttpStatusCode.NotFound);
            return _statusOperations.TryGetValue((host, key), out var saved) ? Response(saved.Receipt) : new(HttpStatusCode.NotFound);
        }
        Assert.AreEqual("{}", Encoding.UTF8.GetString(bytes));
        if (!uri.AbsolutePath.Contains(Tenant.ToString("D"), StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
        var version = request.Headers.GetValues("If-Match").Single(); (bool Active, string Version) current = _statusTenants.GetValueOrDefault(host, (true, "tenant-v1"));
        var fingerprint = uri.AbsolutePath + ":" + version; ManagementIdentityOperationDto receipt;
        if (_statusOperations.TryGetValue((host, key), out var old)) {
            if (fingerprint != old.Fingerprint) return new(HttpStatusCode.Conflict); receipt = old.Receipt;
        } else {
            if (version != current.Version) return new(HttpStatusCode.PreconditionFailed);
            current = (uri.AbsolutePath.EndsWith("/activate", StringComparison.Ordinal), Guid.NewGuid().ToString()); _statusTenants[host] = current; TenantStatusWrites++;
            var now = DateTimeOffset.UtcNow; receipt = new(Guid.NewGuid(), authority, "succeeded", 1, Tenant, now, now, now.AddDays(1)); _statusOperations.Add((host, key), (fingerprint, receipt));
        }
        if (DuringMutation is not null) await DuringMutation(ct);
        if (Failure == "after-commit-timeout") await Task.Delay(Timeout.Infinite, ct);
        if (Failure == "after-commit-malformed") return new(HttpStatusCode.OK) { Content = new StringContent(Canary) };
        if (Failure == "after-commit-503") return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(Canary) };
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = new {
            operation = receipt, tenant = new ManagementCustomerTenantDto(Tenant, "Enterprise", current.Active ? "Active" : "Disabled", receipt.CreatedUtc, receipt.UpdatedUtc, current.Version),
            source = "unknown", externalReference = (string?)null, privateProfile = Canary } }) };
    }
}
