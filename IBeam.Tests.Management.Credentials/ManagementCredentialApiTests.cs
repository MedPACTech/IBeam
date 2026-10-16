using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Diagnostics.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Credentials;

[TestClass]
public sealed class ManagementCredentialApiTests
{
    [TestMethod]
    public async Task DedicatedKeyAuthenticatesOnlyTargetAndLeavesCustomerDefaultUnchanged()
    {
        await using var fixture = await CredentialApiFixture.CreateAsync();
        var issued = await fixture.Credentials.IssueAsync(ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead);
        using var info = await fixture.GetInfoAsync(issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.OK, info.StatusCode);
        var body = await info.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.AreEqual("qurvia", json.RootElement.GetProperty("data").GetProperty("applicationKey").GetString());
        Assert.AreEqual(1, json.RootElement.GetProperty("data").GetProperty("capabilities").GetArrayLength());
        Assert.IsFalse(body.Contains(issued.ApiKey, StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("SecretHash", StringComparison.OrdinalIgnoreCase));
        var scheme = await fixture.App.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync();
        Assert.AreEqual("Customer", scheme!.Name);
        using var ordinary = new HttpRequestMessage(HttpMethod.Get, "/customer-data");
        ordinary.Headers.Add(ManagementCredentialAuthenticationDefaults.HeaderName, issued.ApiKey);
        using var denied = await fixture.Client.SendAsync(ordinary);
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var customer = new HttpRequestMessage(HttpMethod.Get, "/customer-data");
        customer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "customer");
        using var allowed = await fixture.Client.SendAsync(customer);
        Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        var audits = fixture.Audits.Transactions.ToArray();
        Assert.IsTrue(audits.Any(x => x.Action == "management.credentials.authenticate" && x.EntityId == issued.Credential.CredentialId));
        Assert.IsTrue(audits.Any(x => x.Action == ManagementOperations.DiagnosticsInfo && x.ActorId == "management-credential:" + issued.Credential.CredentialId.ToString("D")));
        Assert.IsTrue(audits.All(x => x.CorrelationId!.StartsWith("management-fixture:", StringComparison.Ordinal)));
        var auditJson = JsonSerializer.Serialize(audits);
        Assert.IsFalse(auditJson.Contains(issued.ApiKey, StringComparison.Ordinal));
        Assert.IsTrue(audits.All(x => x.BeforeJson is null && x.AfterJson is null));
    }

    [TestMethod]
    public async Task SingleSchemeAutomaticDefaultCannotGrantCustomerEndpointAccess()
    {
        await using var fixture = await CredentialApiFixture.CreateAsync(customerScheme: false);
        var issued = await fixture.Credentials.IssueAsync();
        using var target = await fixture.GetInfoAsync(issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.OK, target.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/customer-data");
        request.Headers.Add(ManagementCredentialAuthenticationDefaults.HeaderName, issued.ApiKey);
        using var response = await fixture.Client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("customer")]
    [DataRow("human")]
    [DataRow("bearer-key")]
    [DataRow("query-key")]
    [DataRow("wrong-header")]
    [DataRow("duplicate")]
    [DataRow("ambiguous")]
    [DataRow("whitespace")]
    [DataRow("legacy")]
    [DataRow("unknown")]
    [DataRow("wrong-secret")]
    public async Task OtherCredentialsAndAmbiguousInputsCannotAuthenticate(string mode)
    {
        await using var fixture = await CredentialApiFixture.CreateAsync();
        var issued = await fixture.Credentials.IssueAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            mode == "query-key" ? "/api/management-diagnostics/info?apiKey=" + issued.ApiKey : "/api/management-diagnostics/info");
        if (mode is "customer" or "human" or "bearer-key")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mode == "bearer-key" ? issued.ApiKey : mode);
        if (mode == "wrong-header") request.Headers.Add("X-Api-Key", issued.ApiKey);
        if (mode == "duplicate") request.Headers.Add(ManagementCredentialAuthenticationDefaults.HeaderName, new[] { issued.ApiKey, issued.ApiKey });
        if (mode == "ambiguous")
        {
            request.Headers.Add(ManagementCredentialAuthenticationDefaults.HeaderName, issued.ApiKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "customer");
        }
        if (mode == "whitespace") request.Headers.TryAddWithoutValidation(ManagementCredentialAuthenticationDefaults.HeaderName, issued.ApiKey + " extra");
        if (mode == "legacy") request.Headers.Add(ManagementCredentialAuthenticationDefaults.HeaderName, "ibeam_tenant_old_secret");
        if (mode is "unknown" or "wrong-secret")
            request.Headers.Add(ManagementCredentialAuthenticationDefaults.HeaderName,
                new IBeam.Management.Services.Credentials.ManagementCredentialSecretCodec().Create(
                    mode == "unknown" ? Guid.NewGuid() : issued.Credential.CredentialId));
        using var response = await fixture.Client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains(issued.ApiKey, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(ManagementOperations.ErrorsRead)]
    [DataRow(ManagementOperations.LogsRead)]
    public async Task CapabilityDoesNotImplyInfoOrOtherPermissions(string capability)
    {
        await using var fixture = await CredentialApiFixture.CreateAsync();
        var issued = await fixture.Credentials.IssueAsync(capability);
        using var info = await fixture.GetInfoAsync(issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.Forbidden, info.StatusCode);
    }

    [TestMethod]
    [DataRow("revoked")]
    [DataRow("expired")]
    [DataRow("rotated")]
    public async Task ChangedCredentialsFailOnTheNextHttpRequest(string change)
    {
        await using var fixture = await CredentialApiFixture.CreateAsync();
        var issued = await fixture.Credentials.IssueAsync();
        using var valid = await fixture.GetInfoAsync(issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.OK, valid.StatusCode);
        if (change == "revoked") await fixture.Credentials.Provisioning.RevokeAsync(issued.Credential.CredentialId);
        if (change == "rotated") await fixture.Credentials.Provisioning.RotateAsync(issued.Credential.CredentialId);
        if (change == "expired")
        {
            var row = (await fixture.Credentials.Repository.GetAsync("qurvia", "Production", issued.Credential.CredentialId))!;
            await fixture.Credentials.Repository.ReplaceAsync(row with { ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }, row.Version);
        }
        using var denied = await fixture.GetInfoAsync(issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [TestMethod]
    public async Task QueryGuardRereadsRevocationAfterAuthentication()
    {
        await using var fixture = await CredentialApiFixture.CreateAsync(customerScheme: false,
            afterAuthentication: async (f, context) =>
            {
                var id = context.User.FindFirst(ManagementClaimTypes.CredentialId)?.Value;
                if (Guid.TryParse(id, out var credentialId)) await f.Credentials.Provisioning.RevokeAsync(credentialId);
            });
        var issued = await fixture.Credentials.IssueAsync();
        using var denied = await fixture.GetInfoAsync(issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [TestMethod]
    [DataRow("127.0.0.0/8", "127.0.0.1", 200)]
    [DataRow("10.0.0.0/8", "127.0.0.1", 401)]
    [DataRow("127.0.0.0/8", "::ffff:127.0.0.1", 200)]
    [DataRow("2001:db8::/32", "2001:db8::4", 200)]
    [DataRow("2001:db8::/32", "2001:db9::4", 401)]
    [DataRow("127.0.0.0/8", null, 401)]
    public async Task OptionalNetworkRulesUseServerAddress(string network, string? remote, int expected)
    {
        await using var fixture = await CredentialApiFixture.CreateAsync(network, remote);
        var issued = await fixture.Credentials.IssueAsync();
        fixture.Client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.0.0.1");
        using var response = await fixture.GetInfoAsync(issued.ApiKey);
        Assert.AreEqual(expected, (int)response.StatusCode);
    }

    [TestMethod]
    public async Task InvalidNetworkConfigurationFailsStartup()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() => CredentialApiFixture.CreateAsync("arbitrary-header-address"));
    }

    [TestMethod]
    public async Task ProvisioningAndDiagnosticReadersAreNotPubliclyExposed()
    {
        await using var fixture = await CredentialApiFixture.CreateAsync();
        foreach(var path in new[] { "/api/management/credentials/issue", "/api/management/credentials/rotate", "/api/management/credentials/revoke",
            "/api/management-diagnostics/system-errors", "/api/management-diagnostics/system-logs" })
        {
            using var request = new HttpRequestMessage(path.Contains("credentials", StringComparison.Ordinal) ? HttpMethod.Post : HttpMethod.Get, path);
            using var response = await fixture.Client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
    [TestMethod]
    public async Task CredentialStorageOutageFailsClosed()
    {
        await using var fixture = await CredentialApiFixture.CreateAsync();
        var issued = await fixture.Credentials.IssueAsync();
        fixture.Credentials.Repository.SimulateReadOutage = true;
        using var response = await fixture.GetInfoAsync(issued.ApiKey);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual("Service unavailable.", await response.Content.ReadAsStringAsync());
        Assert.IsFalse(fixture.Audits.Transactions.Any(x => x.Action == ManagementOperations.DiagnosticsInfo && x.Succeeded));
    }
}
