using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;

internal sealed partial class CustomerDirectoryBackend
{
    public bool EnableOwnership { get; set; }
    public int OwnershipWrites { get; private set; }
    public Guid DestinationOwner { get; } = Guid.NewGuid();
    private readonly Dictionary<(string Host, string Key), (string Fingerprint, ManagementIdentityOperationDto Receipt)> _ownership = [];
    private async Task<HttpResponseMessage> Ownership(HttpRequestMessage request, string authority, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        if (uri.AbsolutePath.EndsWith("/ownership", StringComparison.Ordinal)) {
            Assert.AreEqual(ManagementCustomerIdentityOperations.OwnersRead, authority);
            return Response(new { tenantId = Failure == "owner-scope" ? Guid.NewGuid() : Tenant, version = "ownership-v1",
                owners = new[] { new { userId = User, membershipVersion = "source-v1", profile = Canary } }, profile = Canary });
        }
        if (request.Method == HttpMethod.Get) {
            var found = _ownership.Where(x => x.Key.Host == uri.Host).Select(x => x.Value.Receipt).FirstOrDefault(x => uri.AbsolutePath.EndsWith(x.OperationId.ToString("D"), StringComparison.Ordinal));
            return found is null ? new(HttpStatusCode.NotFound) : Response(found);
        }
        var bytes = await request.Content!.ReadAsByteArrayAsync(ct); var key = request.Headers.GetValues("Idempotency-Key").Single();
        if (uri.AbsolutePath.EndsWith("/operations/resolve", StringComparison.Ordinal)) {
            LookupCalls++; var lookup = JsonSerializer.Deserialize<ManagementIdentityOperationLookup>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            return lookup.ResourceScope == Tenant && lookup.ExistingResourceId == Tenant && lookup.Operation == authority && _ownership.TryGetValue((uri.Host, key), out var found)
                ? Response(found.Receipt) : new(HttpStatusCode.NotFound);
        }
        var version = request.Headers.GetValues("If-Match").Single();
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) + version;
        ManagementIdentityOperationDto receipt;
        if (_ownership.TryGetValue((uri.Host, key), out var existing)) {
            if (existing.Fingerprint != fingerprint) return new(HttpStatusCode.Conflict); receipt = existing.Receipt;
        } else {
            if (version != "ownership-v1") return new(HttpStatusCode.PreconditionFailed);
            var command = JsonSerializer.Deserialize<TransferManagementCustomerOwnership>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (!command.IsValid() || command.SourceUserId != User || command.DestinationUserId != DestinationOwner || command.SourceVersion != "source-v1" || command.DestinationVersion != "destination-v1") return new(HttpStatusCode.Conflict);
            Assert.AreEqual(ManagementCustomerIdentityOperations.OwnersTransfer, authority); OwnershipWrites++;
            var now = DateTimeOffset.UtcNow; receipt = new(Guid.NewGuid(), authority, "succeeded", 1, Tenant, now, now, now.AddDays(1));
            _ownership[(uri.Host,key)] = (fingerprint,receipt);
        }
        if (DuringMutation is not null) await DuringMutation(ct);
        if (Failure == "after-commit-timeout") await Task.Delay(Timeout.Infinite, ct);
        if (Failure == "after-commit-malformed") return new(HttpStatusCode.OK) { Content = new StringContent(Canary) };
        if (Failure == "after-commit-503") return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(Canary) };
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = new { operation = receipt, profile = Canary } }) };
    }
}
