using System.Net;
using System.Net.Http.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.WebUtilities;
namespace IBeam.Tests.Management.Applications;

// Transport-only fixture. Real signing and target validation are exercised by the target integration suite.
internal sealed class CustomerDirectoryBackend : IManagementDelegatedActorProvider
{
    public static readonly string[] ReadOperations = [ManagementCustomerIdentityOperations.TenantsRead,
        ManagementCustomerIdentityOperations.UsersRead, ManagementCustomerIdentityOperations.InvitesRead];
    public const string Canary = "PRIVATE-PROFILE-TOKEN-AND-PROVIDER-CANARY";
    public Guid Tenant { get; } = Guid.NewGuid();
    public Guid User { get; } = Guid.NewGuid();
    public Guid Invite { get; } = Guid.NewGuid();
    public Dictionary<string, (Guid App, ManagementCustomerIdentityInfo Info, string Key)> Targets { get; } = [];
    public List<(Guid App, string Operation, string Path)> Assertions { get; } = [];
    public int Calls { get; private set; }
    public bool Empty { get; set; }
    public string? Failure { get; set; }
    public Func<CancellationToken, Task>? DuringData { get; set; }
    public async Task<Guid> AddApp(PortalHttpFixture f, string key = "qurvia", string[]? human = null)
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
            new[] { ManagementCustomerIdentityOperations.Info }.Concat(ReadOperations).ToArray());
        f.Data.Credentials.Rows[f.Data.Credentials.Rows.IndexOf(row)] = row with { Capabilities = info.Capabilities, CustomerIdentity = new("available", info) };
        Targets[new Uri(row.BaseUrl).Host] = (id, info, ConnectionFixture.Key + "-" + key);
        return id;
    }
    public Task<ManagementActorAssertion> IssueAsync(Guid app, string operation, ManagementDelegationRequest request, CancellationToken ct = default)
    {
        Assert.AreEqual("GET", request.Method); Assert.AreEqual(0, request.Body.Length);
        Assertions.Add((app, operation, request.PathAndQuery));
        return Task.FromResult(new ManagementActorAssertion { Value = "fixture-actor", ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(30) });
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
            return Response(info); }
        Assert.AreEqual("fixture-actor", request.Headers.GetValues("X-IBeam-Management-Actor").Single());
        var proof = Assertions.Last(); Assert.AreEqual(target.App, proof.App); Assert.AreEqual(uri.PathAndQuery, proof.Path);
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
            _ => new { tenantId = Tenant, name = target.Info.ApplicationKey, status = "Active", version = "v1", profile = Canary } };
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!query.ContainsKey("pageSize")) return Response(row);
        var size = int.Parse(query["pageSize"].ToString());
        if (query.TryGetValue("continuationToken", out var continuation)) Assert.AreEqual("provider-next", continuation.ToString());
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true,
            data = Empty ? Array.Empty<object>() : new[] { row }, pageSize = Failure == "page" ? size + 1 : size,
            continuationToken = query.ContainsKey("continuationToken") ? null : "provider-next" }) };
    }
    private static HttpResponseMessage Response(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data }) };
}
