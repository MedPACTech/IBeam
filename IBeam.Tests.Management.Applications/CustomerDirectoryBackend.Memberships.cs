using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;
internal sealed partial class CustomerDirectoryBackend
{
    public bool EnableMemberships { get; set; }
    public int MembershipWrites { get; private set; }
    private sealed record MemberState(bool Enabled, Guid[] Roles, string Version);
    private readonly Dictionary<string, MemberState> _members = [];
    private readonly Dictionary<(string Host, string Authority, string Key), (string Fingerprint, ManagementIdentityOperationDto Receipt)> _memberOperations = [];
    private async Task<HttpResponseMessage> CustomerMembership(HttpRequestMessage request, string authority, CancellationToken ct)
    {
        var uri = request.RequestUri!; var host = uri.Host;
        if (request.Method == HttpMethod.Get) {
            var row = _memberOperations.Where(x => x.Key.Host == host && x.Key.Authority == authority).Select(x => x.Value.Receipt)
                .FirstOrDefault(x => uri.AbsolutePath.EndsWith(x.OperationId.ToString("D"), StringComparison.Ordinal));
            return row is null ? new(HttpStatusCode.NotFound) : Response(row);
        }
        var bytes = await request.Content!.ReadAsByteArrayAsync(ct); var key = request.Headers.GetValues("Idempotency-Key").Single();
        if (uri.AbsolutePath.EndsWith("/operations/resolve", StringComparison.Ordinal)) {
            LookupCalls++; var lookup = JsonSerializer.Deserialize<ManagementIdentityOperationLookup>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (lookup.ResourceScope != Tenant || lookup.ExistingResourceId != User || lookup.Operation != authority) return new(HttpStatusCode.NotFound);
            return _memberOperations.TryGetValue((host, authority, key), out var existing) ? Response(existing.Receipt) : new(HttpStatusCode.NotFound);
        }
        if (!uri.AbsolutePath.Contains(Tenant.ToString("D"), StringComparison.Ordinal) || !uri.AbsolutePath.Contains(User.ToString("D"), StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
        var assignment = request.Method == HttpMethod.Put;
        Assert.AreEqual(assignment ? ManagementCustomerIdentityOperations.RolesAssign : ManagementCustomerIdentityOperations.MembershipsManage, authority);
        var version = request.Headers.GetValues("If-Match").Single();
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.Concat(System.Text.Encoding.UTF8.GetBytes(version + uri.AbsolutePath)).ToArray()));
        var identity = (host, authority, key); ManagementIdentityOperationDto receipt;
        var current = _members.GetValueOrDefault(host, new(true, [ApprovedRole], "member-v1"));
        if (_memberOperations.TryGetValue(identity, out var old)) {
            if (old.Fingerprint != fingerprint) return new(HttpStatusCode.Conflict); receipt = old.Receipt;
        } else {
            if (version != current.Version) return new(HttpStatusCode.PreconditionFailed);
            if (assignment) {
                var input = JsonSerializer.Deserialize<ChangeManagementCustomerRoles>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                if (input.PolicyKey != "default" || input.RoleIds.Any(x => x != ApprovedRole)) return new(HttpStatusCode.Forbidden);
                current = current with { Roles = input.RoleIds.ToArray() };
            } else current = current with { Enabled = JsonSerializer.Deserialize<ChangeManagementCustomerMembership>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.IsEnabled };
            current = current with { Version = Guid.NewGuid().ToString() }; _members[host] = current; MembershipWrites++;
            var now = DateTimeOffset.UtcNow; receipt = new(Guid.NewGuid(), authority, "succeeded", 1, User, now, now, now.AddDays(1));
            _memberOperations.Add(identity, (fingerprint, receipt));
        }
        if (DuringMutation is not null) await DuringMutation(ct);
        if (Failure == "after-commit-timeout") await Task.Delay(Timeout.Infinite, ct);
        if (Failure == "after-commit-malformed") return new(HttpStatusCode.OK) { Content = new StringContent(Canary) };
        if (Failure == "after-commit-503") return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(Canary) };
        var dto = new ManagementCustomerUserDto(Failure == "member-wrong-tenant" ? Guid.NewGuid() : Tenant, User, null, null, null,
            current.Enabled ? "active" : "disabled", current.Roles, current.Roles.Length == 0 ? [] : ["Member"]) { Version = current.Version };
        var result = JsonSerializer.SerializeToNode(new ManagementCustomerMembershipResult(receipt, dto), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        result["membership"]!["privateProfile"] = Canary;
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = result }) };
    }
}
