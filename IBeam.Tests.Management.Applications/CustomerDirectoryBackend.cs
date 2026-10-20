using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.WebUtilities;
namespace IBeam.Tests.Management.Applications;

// Transport-only fixture. Real signing and target validation are exercised by the target integration suite.
internal sealed partial class CustomerDirectoryBackend : IManagementDelegatedActorProvider
{
    public static readonly string[] ReadOperations = [ManagementCustomerIdentityOperations.TenantsRead,
        ManagementCustomerIdentityOperations.UsersRead, ManagementCustomerIdentityOperations.InvitesRead];
    public const string Canary = "PRIVATE-PROFILE-TOKEN-AND-PROVIDER-CANARY";
    public Guid Tenant { get; } = Guid.NewGuid();
    public Guid User { get; } = Guid.NewGuid();
    public Guid Invite { get; } = Guid.NewGuid();
    public Guid Operation { get; } = Guid.NewGuid();
    public Dictionary<string, (Guid App, ManagementCustomerIdentityInfo Info, string Key)> Targets { get; } = [];
    public List<(Guid App, string Operation, string Path)> Assertions { get; } = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Guid App, string Operation, string Path, string Method, string? Key, string? Version, byte[] Body)> _proofs = [];
    public Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>>? Forward { get; set; }
    public bool EnableMutations { get; set; }
    public int MutationCalls { get; private set; }
    public int MutationWrites { get; private set; }
    public int LookupCalls { get; private set; }
    public List<string> MutationProofs { get; } = [];
    public Func<CancellationToken, Task>? DuringMutation { get; set; }
    private sealed record TenantState(Guid Id, string Name, string Version, string? Reference);
    private sealed record MutationState(string Fingerprint, ManagementIdentityOperationDto Receipt, ManagementCustomerTenantDto Tenant, string? Reference);
    private readonly Dictionary<(string Host, string Operation, string Key, Guid Scope), MutationState> _mutations = [];
    private readonly Dictionary<(string Host, Guid Id), TenantState> _tenantStates = [];
    public int Calls { get; private set; }
    public string TenantSource { get; set; } = "unknown";
    public bool Empty { get; set; }
    public string? Failure { get; set; }
    public Func<CancellationToken, Task>? DuringData { get; set; }
    public async Task<Guid> AddApp(PortalHttpFixture f, string key = "qurvia", string[]? human = null, bool includeOperations = false, bool includeMutations = false, bool includeOnboarding = false, bool includeProvisioning = false, bool includeMemberships = false, bool includeTenantStatus = false)
    {
        var id = await f.Data.AddApp(key);
        var grants = human ?? ReadOperations;
        f.Data.Access.Actor = f.Data.Access.Admin; f.Data.Access.CanAdminister = true;
        foreach (var permission in grants) f.Data.Access.CustomerDelegations.Add((id, permission));
        var old = f.Data.Access.Access.Rows[(f.Data.Access.Org, id, f.Data.Access.Reader)];
        await f.Data.Access.Service.PutAsync(id, f.Data.Access.Reader, new() { Permissions = grants, ExpectedVersion = old.Version });
        f.Data.Access.Actor = f.Data.Access.Reader; f.Data.Access.CanAdminister = false;
        var row = f.Data.Credentials.Rows.Single(x => x.ApplicationId == id);
        var info = new ManagementCustomerIdentityInfo(key, "Production", Guid.NewGuid(), "dedicated-app", 1,
            new[] { ManagementCustomerIdentityOperations.Info }.Concat(ReadOperations)
                .Concat(includeOperations || includeMutations ? new[] { ManagementCustomerIdentityOperations.TenantsCreate, ManagementCustomerIdentityOperations.OnboardingRead } : [])
                .Concat(includeMutations ? new[] { ManagementCustomerIdentityOperations.TenantsUpdate } : [])
                .Concat(includeOnboarding ? ManagementCustomerIdentityPermissionPolicy.RequiredFor(ManagementCustomerIdentityOperations.OnboardingRetry).Concat(new[] { ManagementCustomerIdentityOperations.OnboardingRead }) : [])
                .Concat(includeProvisioning ? ManagementCustomerIdentityPermissionPolicy.RequiredFor(ManagementCustomerIdentityOperations.UsersProvision).Append(ManagementCustomerIdentityOperations.RolesRead) : []).Distinct().ToArray()) {
                    OperationReceiptProtocolVersion = includeMutations || includeOnboarding || includeProvisioning || includeMemberships || includeTenantStatus ? 1 : 0 };
        if (includeMemberships) info = info with { Capabilities = info.Capabilities.Concat(new[] { ManagementCustomerIdentityOperations.MembershipsManage, ManagementCustomerIdentityOperations.RolesAssign }).Distinct().ToArray() };
        if (includeTenantStatus) info = info with { Capabilities = info.Capabilities.Append(ManagementCustomerIdentityOperations.TenantsStatus).Distinct().ToArray() };
        f.Data.Credentials.Rows[f.Data.Credentials.Rows.IndexOf(row)] = row with { Capabilities = info.Capabilities, CustomerIdentity = new("available", info) };
        Targets[new Uri(row.BaseUrl).Host] = (id, info, ConnectionFixture.Key + "-" + key);
        return id;
    }
    public Task<ManagementActorAssertion> IssueAsync(Guid app, string operation, ManagementDelegationRequest request, CancellationToken ct = default)
    {
        if (request.Method == "GET") { Assert.AreEqual(0, request.Body.Length); Assert.IsNull(request.IdempotencyKey); Assert.IsNull(request.IfMatch); }
        else Assert.IsTrue(IBeam.Management.Services.CustomerIdentity.Delegation.ManagementDelegationBinding.ValidIdempotencyKey(request.IdempotencyKey));
        var value = "fixture-actor-" + Guid.NewGuid().ToString("N");
        lock (Assertions) Assertions.Add((app, operation, request.PathAndQuery));
        _proofs[value] = (app, operation, request.PathAndQuery, request.Method, request.IdempotencyKey, request.IfMatch, request.Body.ToArray());
        return Task.FromResult(new ManagementActorAssertion { Value = value, ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(30) });
    }
    public sealed class Handler(CustomerDirectoryBackend backend) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => backend.Handle(request, ct); }
    private async Task<HttpResponseMessage> Handle(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        var uri = request.RequestUri!; var target = Targets[uri.Host];
        Assert.AreEqual(target.Key, request.Headers.GetValues("X-IBeam-Management-Key").Single());
        foreach (var header in new[] { "Authorization", "Cookie", "traceparent", "tracestate", "baggage" }) Assert.IsFalse(request.Headers.Contains(header));
        if (uri.AbsolutePath.EndsWith("/info", StringComparison.Ordinal)) {
            Assert.IsFalse(request.Headers.Contains("X-IBeam-Management-Actor"));
            var info = Failure == "realm" ? target.Info with { IdentityRealmId = Guid.NewGuid() } : target.Info;
            if (Failure == "old-receipt-protocol") info = info with { OperationReceiptProtocolVersion = 0 };
            return Response(info); }
        var assertion = request.Headers.GetValues("X-IBeam-Management-Actor").Single();
        var proof = _proofs[assertion]; Assert.AreEqual(target.App, proof.App); Assert.AreEqual(uri.PathAndQuery, proof.Path); Assert.AreEqual(request.Method.Method, proof.Method);
        CollectionAssert.AreEqual(proof.Body, request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(ct));
        Assert.AreEqual(proof.Key, request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null);
        Assert.AreEqual(proof.Version, request.Headers.TryGetValues("If-Match", out var versions) ? versions.Single() : null);
        if (Forward is not null) return await Forward(request, proof.Operation, ct);
        if (EnableTenantStatus && (uri.AbsolutePath.EndsWith("/activate", StringComparison.Ordinal) || uri.AbsolutePath.EndsWith("/deactivate", StringComparison.Ordinal) ||
            uri.AbsolutePath.Contains("/operations/", StringComparison.Ordinal))) return await TenantStatus(request, proof.Operation, ct);
        if (EnableMemberships && (uri.AbsolutePath.EndsWith("/membership", StringComparison.Ordinal) || uri.AbsolutePath.EndsWith("/roles", StringComparison.Ordinal) ||
            uri.AbsolutePath.Contains("/operations/", StringComparison.Ordinal))) return await CustomerMembership(request, proof.Operation, ct);
        if (EnableProvisioning && (uri.AbsolutePath.Contains("/assignable-roles", StringComparison.Ordinal) || uri.AbsolutePath.Contains("/users/provision", StringComparison.Ordinal) ||
            uri.AbsolutePath.Contains("/invites", StringComparison.Ordinal) || uri.AbsolutePath.Contains("/operations/", StringComparison.Ordinal)))
            return await CustomerProvisioning(request, proof.Operation, ct);
        if (EnableOnboarding && (uri.AbsolutePath.Contains("/enterprise-onboardings", StringComparison.Ordinal) || uri.AbsolutePath.Contains("/operations/", StringComparison.Ordinal)))
            return await Onboarding(request, proof.Operation, ct);
        if (EnableMutations && (uri.AbsolutePath == "/api/management-identity/operations/resolve" || request.Method != HttpMethod.Get))
            return await Mutate(request, proof.Operation, assertion, ct);
        if (EnableMutations && uri.AbsolutePath.Contains("/operations/", StringComparison.Ordinal)) {
            lock (_mutations) {
                var found = _mutations.Where(x => x.Key.Host == uri.Host).Select(x => x.Value).SingleOrDefault(x => uri.AbsolutePath.EndsWith(x.Receipt.OperationId.ToString("D"), StringComparison.Ordinal));
                return found is null ? new(HttpStatusCode.NotFound) : Response(found.Receipt);
            }
        }
        if (uri.AbsolutePath.Contains("/operations/", StringComparison.Ordinal)) {
            Assert.AreEqual(ManagementCustomerIdentityOperations.TenantsCreate, proof.Operation);
            var now = DateTimeOffset.UtcNow;
            return Response(new { operationId = Failure == "receipt-id" ? Guid.NewGuid() : Operation,
                operation = Failure == "receipt-authority" ? ManagementCustomerIdentityOperations.TenantsUpdate : ManagementCustomerIdentityOperations.TenantsCreate,
                status = "succeeded", step = Failure == "receipt-step" ? 0 : 1, resourceId = Tenant, createdUtc = now.AddMinutes(-2), updatedUtc = now,
                expiresUtc = now.AddDays(1), fingerprint = Canary, body = Canary });
        }
        var resource = uri.AbsolutePath.Contains("/users", StringComparison.Ordinal) ? "users" : uri.AbsolutePath.Contains("/invites", StringComparison.Ordinal) ? "invites" : "tenants";
        Assert.AreEqual("management.identity." + resource + ".read", proof.Operation);
        if (DuringData is not null) await DuringData(ct);
        if (int.TryParse(Failure, out var status)) return new((HttpStatusCode)status) { Content = new StringContent(Canary) };
        if (Failure == "malformed") return new(HttpStatusCode.OK) { Content = new StringContent(Canary) };
        if (Failure == "oversize") return new(HttpStatusCode.OK) { Content = new StringContent(new string('a', 1048577)) };
        var tenant = Failure == "scope" ? Guid.NewGuid() : Tenant;
        object row = resource switch {
            "users" => new { tenantId = tenant, userId = User, email = target.Info.ApplicationKey + "@example.test", firstName = (string?)null,
                lastName = (string?)null, status = "active", roleIds = Array.Empty<Guid>(), roleNames = Array.Empty<string>(), version = "v1", tokenHash = Canary, profile = Canary },
            "invites" => new { tenantId = tenant, inviteId = Invite, email = target.Info.ApplicationKey + "@example.test", firstName = (string?)null,
                lastName = (string?)null, status = "pending", roleIds = Array.Empty<Guid>(), roleNames = Array.Empty<string>(), version = "v1", token = Canary, redirect = Canary },
            _ => new { tenantId = Tenant, name = target.Info.ApplicationKey, status = "Active", version = "v1", source = TenantSource, profile = Canary } };
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!query.ContainsKey("pageSize")) return Response(row);
        var size = int.Parse(query["pageSize"].ToString());
        if (query.TryGetValue("continuationToken", out var continuation)) Assert.AreEqual("provider-next", continuation.ToString());
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true,
            data = Empty ? Array.Empty<object>() : new[] { row }, pageSize = Failure == "page" ? size + 1 : size,
            continuationToken = query.ContainsKey("continuationToken") ? null : "provider-next" }) };
    }
    private static HttpResponseMessage Response(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data }) };
    private async Task<HttpResponseMessage> Mutate(HttpRequestMessage request, string operation, string assertion, CancellationToken ct)
    {
        var uri = request.RequestUri!; var key = request.Headers.GetValues("Idempotency-Key").Single();
        var bytes = await request.Content!.ReadAsByteArrayAsync(ct);
        if (uri.AbsolutePath == "/api/management-identity/operations/resolve") {
            var lookup = JsonSerializer.Deserialize<ManagementIdentityOperationLookup>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.AreEqual(operation, lookup.Operation); LookupCalls++;
            lock (_mutations) return _mutations.TryGetValue((uri.Host, operation, key, lookup.ResourceScope), out var found) ? Response(found.Receipt) : new(HttpStatusCode.NotFound);
        }
        MutationState state;
        var create = request.Method == HttpMethod.Post;
        var id = create ? Guid.Empty : Guid.Parse(uri.Segments.Last());
        Assert.AreEqual(create ? ManagementCustomerIdentityOperations.TenantsCreate : ManagementCustomerIdentityOperations.TenantsUpdate, operation);
        var version = request.Headers.TryGetValues("If-Match", out var values) ? values.Single() : null;
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) + ":" + version;
        using var body = JsonDocument.Parse(bytes); var name = body.RootElement.GetProperty("name").GetString()!;
        string? reference = create && body.RootElement.TryGetProperty("externalReference", out var ext) ? ext.GetString() : null;
        lock (_mutations) {
            MutationCalls++; MutationProofs.Add(assertion);
            var identity = (uri.Host, operation, key, id);
            if (_mutations.TryGetValue(identity, out var old)) {
                if (old.Fingerprint != fingerprint) return new(HttpStatusCode.Conflict) { Content = new StringContent(Canary) };
                state = old;
            } else {
                if (create) id = _tenantStates.ContainsKey((uri.Host, Tenant)) ? Guid.NewGuid() : Tenant;
                else if (!_tenantStates.TryGetValue((uri.Host, id), out var existing)) return new(HttpStatusCode.NotFound);
                else if (existing.Version != version) return new(HttpStatusCode.PreconditionFailed) { Content = new StringContent(Canary) };
                var current = new TenantState(id, name, Guid.NewGuid().ToString(), reference ?? (_tenantStates.TryGetValue((uri.Host, id), out var existingRow) ? existingRow.Reference : null));
                _tenantStates[(uri.Host, id)] = current; MutationWrites++;
                var now = DateTimeOffset.UtcNow;
                var receipt = new ManagementIdentityOperationDto(Guid.NewGuid(), operation, "succeeded", 1, id, now, now, now.AddDays(1));
                state = new(fingerprint, receipt, new(id, name, "Active", now, now, current.Version), current.Reference);
                _mutations.Add(identity, state);
            }
        }
        if (DuringMutation is not null) await DuringMutation(ct);
        if (Failure == "after-commit-timeout") await Task.Delay(Timeout.Infinite, ct);
        if (Failure == "after-commit-malformed") return new(HttpStatusCode.Created) { Content = new StringContent(Canary) };
        if (Failure == "after-commit-503") return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(Canary) };
        return new(create ? HttpStatusCode.Created : HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true,
            data = new { operation = state.Receipt, tenant = state.Tenant, source = "support-provisioned", externalReference = state.Reference, profile = Canary, password = Canary } }) };
    }
}
