using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class DelegationTests
{
    [TestMethod]
    public async Task RealSignedReadPreservesSeparateSupportActorAndMachineIdentityInAudit()
    {
        await using var f = await DelegationFixture.Start();
        var assertion = await f.Issue(ManagementCustomerIdentityOperations.TenantsRead);
        using var response = await f.Send(assertion);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(f.User, data.GetProperty("userId").GetGuid());
        Assert.AreEqual(f.Credentials.Row.CredentialId, data.GetProperty("credentialId").GetGuid());
        Assert.AreEqual("credential", data.GetProperty("subjectType").GetString());
        var audit = f.Audit.Rows.Where(x => x.Operation == "management.identity.delegation.require").ToArray();
        Assert.IsTrue(audit.Any(x => x.ActorUserId == f.User && x.OrganizationId == f.Organization &&
            x.CredentialId == f.Credentials.Row.CredentialId && x.ApplicationId == f.Application));
        var text = JsonSerializer.Serialize(f.Audit.Rows);
        Assert.DoesNotContain(f.Key, text); Assert.DoesNotContain(assertion.Value, text);
        Assert.DoesNotContain(assertion.Value, JsonSerializer.Serialize(assertion));
        Assert.DoesNotContain(assertion.Value, assertion.ToString());
    }
    [TestMethod]
    public async Task MutationAssertionIsSingleUseAndFreshRetryKeepsTheSameRequestBody()
    {
        await using var f = await DelegationFixture.Start();
        var body = Encoding.UTF8.GetBytes("{ \"message\": \"CUSTOMER-PAYLOAD-CANARY\" }");
        var assertion = await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", body: body);
        using var first = await f.Send(assertion, "POST", body: body);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());
        using var replay = await f.Send(assertion, "POST", body: body);
        Assert.AreEqual(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.AreEqual(1, f.Executions);
        var retry = await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", body: body);
        using var second = await f.Send(retry, "POST", body: body);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual(2, f.Executions);
        Assert.DoesNotContain("CUSTOMER-PAYLOAD-CANARY", JsonSerializer.Serialize(f.Audit.Rows));
        Assert.DoesNotContain("binding", JsonSerializer.Serialize(f.Audit.Rows));
    }
    [TestMethod]
    [DataRow("issuer")] [DataRow("audience")] [DataRow("organization")] [DataRow("application")] [DataRow("realm")]
    [DataRow("credential")] [DataRow("environment")] [DataRow("operation")] [DataRow("binding")] [DataRow("user")]
    [DataRow("expired")] [DataRow("future")] [DataRow("long-lived")] [DataRow("type")] [DataRow("key")] [DataRow("algorithm")]
    [DataRow("signature")] [DataRow("missing")] [DataRow("body")] [DataRow("path")]
    public async Task SubstitutionAndMalformedAssertionsAreDeniedBeforeTheOperation(string change)
    {
        await using var f = await DelegationFixture.Start();
        var assertion = await f.Issue(ManagementCustomerIdentityOperations.TenantsRead);
        RSA? other = null;
        try
        {
            if (change == "signature")
            {
                var parts = assertion.Value.Split('.');
                parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
                assertion = new() { Value = string.Join('.', parts), ExpiresUtc = assertion.ExpiresUtc };
            }
            else if (change == "key")
            {
                other = RSA.Create(2048);
                assertion = f.Change(assertion, _ => {}, new(new RsaSecurityKey(other) { KeyId = "unknown-key" }, SecurityAlgorithms.RsaSha256));
            }
            else if (change == "algorithm")
                assertion = f.Change(assertion, _ => {}, new(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "active-key" }, SecurityAlgorithms.HmacSha256));
            else if (change is not ("missing" or "body" or "path"))
                assertion = f.Change(assertion, p => {
                    switch (change)
                    {
                        case "issuer": p["iss"] = "untrusted-issuer"; break;
                        case "audience": p["aud"] = "another-target"; break;
                        case "organization": p["org"] = Guid.NewGuid().ToString(); break;
                        case "application": p["app"] = Guid.NewGuid().ToString(); break;
                        case "realm": p["realm"] = Guid.NewGuid().ToString(); break;
                        case "credential": p["credential"] = Guid.NewGuid().ToString(); break;
                        case "environment": p["environment"] = "Production"; break;
                        case "operation": p["operation"] = ManagementCustomerIdentityOperations.TenantsCreate; break;
                        case "binding": p["binding"] = new string('0', 64); break;
                        case "user": p["sub"] = ""; break;
                        case "expired": p["exp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1; break;
                        case "future": p["nbf"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 20; break;
                        case "long-lived": p["exp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 120; break;
                    }
                }, type: change == "type" ? "JWT" : null);
            using var response = await f.Send(change == "missing" ? null : assertion,
                path: change == "path" ? "/api/management-identity/probe?different-tenant=true" : "/api/management-identity/probe",
                body: change == "body" ? Encoding.UTF8.GetBytes("{}") : null);
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, change + ": " + await response.Content.ReadAsStringAsync());
            Assert.AreEqual(0, f.Executions);
        }
        finally { other?.Dispose(); }
    }
    [TestMethod]
    public async Task LiveMachineRevocationAndTargetPolicyStillApplyAfterAnAssertionWasSigned()
    {
        await using var f = await DelegationFixture.Start();
        var assertion = await f.Issue(ManagementCustomerIdentityOperations.TenantsRead);
        f.Credentials.Row = f.Credentials.Row with { RevokedUtc = DateTimeOffset.UtcNow };
        using var revoked = await f.Send(assertion);
        Assert.AreEqual(HttpStatusCode.Unauthorized, revoked.StatusCode);
        f.Credentials.Row = f.Credentials.Row with { RevokedUtc = null };
        f.App.Services.GetRequiredService<IOptions<ManagementCustomerIdentityTargetOptions>>().Value.EnabledOperations = [];
        using var disabledPolicy = await f.Send(assertion);
        Assert.AreEqual(HttpStatusCode.Unauthorized, disabledPolicy.StatusCode);
    }
    [TestMethod]
    public async Task RemovedHumanGrantStopsNewAssertionsAndDirectServicesNeedVerifiedContext()
    {
        await using var f = await DelegationFixture.Start();
        f.Granted = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Issue(ManagementCustomerIdentityOperations.TenantsRead));
        using var scope = f.App.Services.CreateScope();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => scope.ServiceProvider
            .GetRequiredService<IManagementCustomerIdentityTargetAccessService>().RequireAsync(ManagementCustomerIdentityOperations.TenantsRead));
    }
    [TestMethod]
    public async Task RemovingTheOldVerificationKeyImmediatelyRejectsOldAssertions()
    {
        await using var f = await DelegationFixture.Start();
        var assertion = await f.Issue(ManagementCustomerIdentityOperations.TenantsRead);
        using var newKey = RSA.Create(2048);
        f.VerificationKeys.Clear();
        f.VerificationKeys.Add(new RsaSecurityKey(newKey.ExportParameters(false)) { KeyId = "new-key" });
        using var response = await f.Send(assertion);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [TestMethod]
    [DataRow("header-url")] [DataRow("duplicate-actor")]
    public async Task SignedButAmbiguousMetadataAndRemoteKeyHeadersAreRejected(string mode)
    {
        await using var f = await DelegationFixture.Start();
        var assertion = await f.Issue(ManagementCustomerIdentityOperations.TenantsRead);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(assertion.Value);
        var headerJson = "{\"alg\":\"RS256\",\"kid\":\"active-key\",\"typ\":\"ibeam-management-actor+jwt\"" +
            (mode == "header-url" ? ",\"jku\":\"https://untrusted.example/keys\"}" : "}");
        var payload = token.Payload.SerializeToJson();
        if (mode == "duplicate-actor") payload = payload[..^1] + ",\"sub\":\"" + Guid.NewGuid() + "\"}";
        var input = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(headerJson)) + "." + Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload));
        var signature = f.Rsa.SignData(Encoding.UTF8.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var response = await f.Send(new() { Value = input + "." + Base64UrlEncoder.Encode(signature), ExpiresUtc = assertion.ExpiresUtc });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [TestMethod]
    public async Task ConcurrentMutationReplaysReachTheBusinessOperationOnlyOnce()
    {
        await using var f = await DelegationFixture.Start();
        var assertion = await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST");
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => f.Send(assertion, "POST")));
        try
        {
            Assert.AreEqual(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
            Assert.AreEqual(4, responses.Count(x => x.StatusCode == HttpStatusCode.Unauthorized));
            Assert.AreEqual(1, f.Executions);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    [TestMethod]
    public void RequestBindingRejectsUnboundedOrAmbiguousInput()
    {
        foreach (var path in new[] { "/customer", "/api/management-identity/probe#fragment", "/api/management-identity/probe\r\nInjected" })
            Assert.Throws<ManagementAccessDeniedException>(() => ManagementDelegationBinding.Hash(new() { Method = "GET", PathAndQuery = path }));
        Assert.Throws<ManagementAccessDeniedException>(() => ManagementDelegationBinding.Hash(new() { Method = "GET",
            PathAndQuery = "/api/management-identity/probe", Body = new byte[32769] }));
    }
}
