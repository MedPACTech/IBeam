using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;

internal sealed partial class CustomerDirectoryBackend
{
    public bool EnableProvisioning { get; set; }
    public Guid ApprovedRole { get; } = Guid.NewGuid();
    public int ProvisionWrites { get; private set; }
    public int InvitationSends { get; private set; }
    public string CustomerInvitationStatus { get; set; } = "pending";
    public string CustomerDeliveryStatus { get; set; } = "sent";
    private readonly Dictionary<(string Host, string Authority, string Key, Guid Resource), (string Fingerprint, ManagementIdentityOperationDto Receipt)> _customerWrites = [];
    private readonly Dictionary<string, string> _inviteVersions = [];
    private readonly Dictionary<string, string> _customerEmails = [];
    private async Task<HttpResponseMessage> CustomerProvisioning(HttpRequestMessage request, string authority, CancellationToken ct)
    {
        var uri = request.RequestUri!; var host = uri.Host;
        if (uri.AbsolutePath.EndsWith("/assignable-roles", StringComparison.Ordinal)) {
            Assert.AreEqual(ManagementCustomerIdentityOperations.RolesRead, authority);
            if (DuringData is not null) await DuringData(ct);
            if (!uri.AbsolutePath.Contains(Tenant.ToString("D"), StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            return Response(new[] { new ManagementAssignableRoleDto(Tenant, ApprovedRole, "Member", ["default"]) });
        }
        if (request.Method == HttpMethod.Get && uri.AbsolutePath.Contains("/invites", StringComparison.Ordinal)) {
            Assert.AreEqual(ManagementCustomerIdentityOperations.InvitesRead, authority);
            var invitation = SafeInvite(host);
            if (uri.Query.Contains("pageSize", StringComparison.Ordinal)) return new(HttpStatusCode.OK) {
                Content = JsonContent.Create(new { success = true, data = new[] { invitation }, pageSize = 10, continuationToken = (string?)null }) };
            return Response(invitation);
        }
        if (request.Method == HttpMethod.Get) {
            var row = _customerWrites.Where(x => x.Key.Host == host && x.Key.Authority == authority).Select(x => x.Value.Receipt)
                .FirstOrDefault(x => uri.AbsolutePath.EndsWith(x.OperationId.ToString("D"), StringComparison.Ordinal));
            return row is null ? new(HttpStatusCode.NotFound) : Response(ProjectCustomer(row, host));
        }
        var bytes = await request.Content!.ReadAsByteArrayAsync(ct); var key = request.Headers.GetValues("Idempotency-Key").Single();
        if (uri.AbsolutePath.EndsWith("/operations/resolve", StringComparison.Ordinal)) {
            LookupCalls++; var lookup = JsonSerializer.Deserialize<ManagementIdentityOperationLookup>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.AreEqual(authority, lookup.Operation);
            return _customerWrites.TryGetValue((host, authority, key, lookup.ExistingResourceId!.Value), out var known)
                ? Response(ProjectCustomer(known.Receipt, host)) : new(HttpStatusCode.NotFound);
        }
        if (!uri.AbsolutePath.Contains(Tenant.ToString("D"), StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
        var provision = uri.AbsolutePath.EndsWith("/users/provision", StringComparison.Ordinal); var revoke = uri.AbsolutePath.EndsWith("/revoke", StringComparison.Ordinal);
        Assert.AreEqual(provision ? ManagementCustomerIdentityOperations.UsersProvision : ManagementCustomerIdentityOperations.InvitesManage, authority);
        var version = request.Headers.TryGetValues("If-Match", out var versions) ? versions.Single() : "";
        var identity = (host, authority, key, provision ? Tenant : Invite);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.Concat(Encoding.UTF8.GetBytes(uri.AbsolutePath + version)).ToArray()));
        ManagementIdentityOperationDto receipt;
        if (_customerWrites.TryGetValue(identity, out var previous)) {
            if (previous.Fingerprint != fingerprint) return new(HttpStatusCode.Conflict); receipt = previous.Receipt;
        } else {
            if (provision) {
                var input = JsonSerializer.Deserialize<ProvisionManagementCustomerUser>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                if (input.PolicyKey != "default" || !input.RoleIds.SequenceEqual(new[] { ApprovedRole })) return new(HttpStatusCode.Forbidden);
                _customerEmails[host] = input.Email;
                ProvisionWrites++; InvitationSends++;
            } else {
                Assert.AreEqual("{}", Encoding.UTF8.GetString(bytes));
                if (version != _inviteVersions.GetValueOrDefault(host, "invite-v1")) return new(HttpStatusCode.PreconditionFailed);
                if (CustomerInvitationStatus is "redeemed" or "revoked") return new(HttpStatusCode.Conflict);
                if (revoke) CustomerInvitationStatus = "revoked"; else InvitationSends++;
            }
            _inviteVersions[host] = Guid.NewGuid().ToString(); var now = DateTimeOffset.UtcNow;
            receipt = new(Guid.NewGuid(), authority, "succeeded", 1, provision ? Tenant : Invite, now, now, now.AddDays(1));
            _customerWrites[identity] = (fingerprint, receipt);
        }
        if (DuringMutation is not null) await DuringMutation(ct);
        if (Failure == "after-commit-timeout") await Task.Delay(Timeout.Infinite, ct);
        if (Failure == "after-commit-503") return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(Canary) };
        if (Failure == "after-commit-malformed") return new(HttpStatusCode.Accepted) { Content = new StringContent(Canary) };
        var dto = SafeInvite(host); var details = ProjectCustomer(receipt, host);
        object result = provision ? new ManagementCustomerProvisioningResult(details, Outcome(), dto) : new ManagementCustomerInvitationResult(details, Outcome(), dto);
        var value = JsonSerializer.SerializeToNode(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        value["inviteToken"] = Canary; value["invitation"]!["redirectUrl"] = Canary;
        return new(provision ? HttpStatusCode.Accepted : HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = value }) };
    }
    private string Outcome() => CustomerInvitationStatus switch { "redeemed" => "accepted", "expired" => "expired", "revoked" => "revoked", _ => CustomerDeliveryStatus };
    private ManagementIdentityOperationDto ProjectCustomer(ManagementIdentityOperationDto row, string host) => row with {
        Invitation = new(Failure == "customer-wrong-tenant" ? Guid.NewGuid() : Tenant, Invite,
            Failure == "customer-private-status" ? Canary : CustomerInvitationStatus == "redeemed" ? "accepted" : CustomerInvitationStatus,
            CustomerDeliveryStatus, _inviteVersions.GetValueOrDefault(host, "invite-v1")) };
    private ManagementCustomerInvitationDto SafeInvite(string host) => new(Tenant, Invite,
        Failure == "customer-wrong-recipient" ? "unrequested@example.test" : _customerEmails.GetValueOrDefault(host, Targets[host].Info.ApplicationKey + "@example.test"), null, null,
        CustomerInvitationStatus, [Failure == "customer-wrong-roles" ? Guid.NewGuid() : ApprovedRole], ["Member"], DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), null, _inviteVersions.GetValueOrDefault(host, "invite-v1"));
}
