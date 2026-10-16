using System.Net;
using System.Security.Claims;
using IBeam.Management;
using IBeam.Management.Services;
using IBeam.Management.Services.Credentials;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Credentials;

[TestClass]
public sealed class ManagementDiagnosticsAccessTests
{
    [TestMethod]
    [DataRow(ManagementOperations.ErrorsRead, ManagementOperations.LogsRead)]
    [DataRow(ManagementOperations.LogsRead, ManagementOperations.ErrorsRead)]
    public async Task PermissionsAreIndependentAndClaimsCannotWidenStoredGrants(string allowed, string denied)
    {
        await using var fixture = new CredentialFixture();
        var issued = await fixture.IssueAsync(allowed);
        var principal = new Principal { Value = await AuthenticateAsync(fixture, issued.ApiKey) };
        var access = Access(fixture, principal);
        var result = await access.RequireAsync(allowed);
        Assert.AreEqual(issued.Credential.CredentialId, result.CredentialId);
        // Even a trusted identity accidentally enriched with extra grants/owner must satisfy persisted grants.
        ((ClaimsIdentity)principal.Value.Identity!).AddClaim(new Claim(ManagementClaimTypes.Permission, denied));
        ((ClaimsIdentity)principal.Value.Identity!).AddClaim(new Claim("role", "owner"));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => access.RequireAsync(denied));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => access.RequireAsync("*"));
    }

    [TestMethod]
    [DataRow("human")]
    [DataRow("application")]
    [DataRow("environment")]
    [DataRow("version")]
    [DataRow("missing-permission")]
    [DataRow("duplicate-id")]
    public async Task DirectServiceGuardsRejectSubstitutedContext(string change)
    {
        await using var fixture = new CredentialFixture();
        var issued = await fixture.IssueAsync(ManagementOperations.ErrorsRead);
        var claims = (await AuthenticateAsync(fixture, issued.ApiKey)).Claims.ToList();
        var claimType = change switch
        {
            "human" => ManagementClaimTypes.SubjectType,
            "application" => ManagementClaimTypes.ApplicationKey,
            "environment" => ManagementClaimTypes.Environment,
            "version" => ManagementClaimTypes.CredentialVersion,
            _ => ManagementClaimTypes.Permission
        };
        if (change == "duplicate-id") claims.Add(new Claim(ManagementClaimTypes.CredentialId, issued.Credential.CredentialId.ToString("D")));
        else
        {
            claims.RemoveAll(c => c.Type == claimType);
            if (change != "missing-permission") claims.Add(new Claim(claimType, change == "human" ? ManagementSubjectTypes.Human : "other"));
        }
        var principal = new Principal { Value = new ClaimsPrincipal(new ClaimsIdentity(claims, "TrustedFixture")) };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => Access(fixture, principal).RequireAsync(ManagementOperations.ErrorsRead));
    }

    [TestMethod]
    [DataRow("rotate")]
    [DataRow("revoke")]
    [DataRow("expire")]
    [DataRow("remove-capability")]
    public async Task EveryCallRechecksCurrentRecordAndRejectsAStalePrincipal(string change)
    {
        await using var fixture = new CredentialFixture();
        var issued = await fixture.IssueAsync(ManagementOperations.ErrorsRead);
        var principal = new Principal { Value = await AuthenticateAsync(fixture, issued.ApiKey) };
        var access = Access(fixture, principal);
        await access.RequireAsync(ManagementOperations.ErrorsRead);
        if (change == "rotate") await fixture.Provisioning.RotateAsync(issued.Credential.CredentialId);
        if (change == "revoke") await fixture.Provisioning.RevokeAsync(issued.Credential.CredentialId);
        if (change is "expire" or "remove-capability")
        {
            var row = (await fixture.Repository.GetAsync("qurvia", "Production", issued.Credential.CredentialId))!;
            await fixture.Repository.ReplaceAsync(change == "expire"
                ? row with { ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }
                : row with { Capabilities = [ManagementOperations.LogsRead] }, row.Version);
        }
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => access.RequireAsync(ManagementOperations.ErrorsRead));
    }

    private static Task<ClaimsPrincipal> AuthenticateAsync(CredentialFixture fixture, string key)
        => new ManagementCredentialAuthenticationService(fixture.Validator,
            new ManagementCredentialNetworkPolicy(Options.Create(new ManagementCredentialNetworkOptions())),
            new ManagementCredentialSecretCodec(), new ServiceOperationExecutor()).AuthenticateAsync(key, IPAddress.Loopback);
    private static ManagementDiagnosticsAccessService Access(CredentialFixture fixture, IManagementPrincipalProvider principal)
        => new(principal, fixture.Repository, Options.Create(new ManagementDiagnosticsOptions
        { ApplicationKey = "qurvia", Environment = "Production" }), new ServiceOperationExecutor());
    private sealed class Principal : IManagementPrincipalProvider
    {
        public required ClaimsPrincipal Value { get; init; }
        public ClaimsPrincipal GetPrincipal() => Value;
    }
}
