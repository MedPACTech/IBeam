using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Services.Credentials;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management.Credentials;

[TestClass]
public sealed class ManagementCredentialServiceTests
{
    [TestMethod]
    public async Task IssueRotateRevokePersistOnlyHashesAndImmediatelyInvalidateOldSecrets()
    {
        await using var fixture = new CredentialFixture();
        var issued = await fixture.IssueAsync(ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead);
        var row = (await fixture.Repository.GetAsync("qurvia", "Production", issued.Credential.CredentialId))!;
        Assert.IsFalse(row.SecretHash.Contains(issued.ApiKey, StringComparison.Ordinal));
        Assert.AreEqual(fixture.OperatorId, row.CreatedByOperatorId);
        Assert.IsNotNull(await fixture.Validator.ValidateAsync(issued.ApiKey));
        var rotated = await fixture.Provisioning.RotateAsync(row.CredentialId);
        Assert.AreNotEqual(issued.ApiKey, rotated.ApiKey);
        Assert.IsNull(await fixture.Validator.ValidateAsync(issued.ApiKey));
        Assert.IsNotNull(await fixture.Validator.ValidateAsync(rotated.ApiKey));
        var changed = (await fixture.Repository.GetAsync("qurvia", "Production", row.CredentialId))!;
        Assert.AreEqual(fixture.OperatorId, changed.RotatedByOperatorId);
        Assert.IsNotNull(changed.RotatedUtc);
        await fixture.Provisioning.RevokeAsync(row.CredentialId);
        await fixture.Provisioning.RevokeAsync(row.CredentialId);
        Assert.IsNull(await fixture.Validator.ValidateAsync(rotated.ApiKey));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => fixture.Provisioning.RotateAsync(row.CredentialId));
        var revoked = (await fixture.Repository.GetAsync("qurvia", "Production", row.CredentialId))!;
        Assert.AreEqual("Revoked", revoked.Status);
        Assert.IsNotNull(revoked.RevokedUtc);
        Assert.AreEqual(fixture.OperatorId, revoked.RevokedByOperatorId);
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task ProvisioningRequiresBothOptInAndTrustedOperator(bool enabled, bool authorized)
    {
        await using var fixture = new CredentialFixture(enabled, authorized);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => fixture.IssueAsync());
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => fixture.Provisioning.RotateAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => fixture.Provisioning.RevokeAsync(Guid.NewGuid()));
    }

    [TestMethod]
    [DataRow("expired")]
    [DataRow("revoked")]
    [DataRow("inactive")]
    [DataRow("application")]
    [DataRow("environment")]
    [DataRow("capability")]
    [DataRow("hash")]
    public async Task LiveRecordMustRemainValidForConfiguredDeployment(string change)
    {
        await using var fixture = new CredentialFixture();
        var issued = await fixture.IssueAsync();
        var row = (await fixture.Repository.GetAsync("qurvia", "Production", issued.Credential.CredentialId))!;
        var modified = change switch
        {
            "expired" => row with { ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1) },
            "revoked" => row with { RevokedUtc = DateTimeOffset.UtcNow },
            "inactive" => row with { Status = "Disabled" },
            "capability" => row with { Capabilities = ["*"] },
            "hash" => row with { SecretHash = "malformed" },
            _ => row
        };
        await fixture.Repository.ReplaceAsync(modified, row.Version);
        var validator = change is "application" or "environment"
            ? new ManagementCredentialValidator(fixture.Repository, new ManagementCredentialSecretCodec(),
                Options.Create(new ManagementDiagnosticsOptions
                {
                    ApplicationKey = change == "application" ? "bindry" : "qurvia",
                    Environment = change == "environment" ? "Test" : "Production"
                }), new ServiceOperationExecutor())
            : fixture.Validator;
        Assert.IsNull(await validator.ValidateAsync(issued.ApiKey));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Bearer customer.jwt.token")]
    [DataRow("ibeam_abc_def_secret")]
    [DataRow("ibmgmt_v2.invalid")]
    public async Task WrongCredentialFormatsFailWithoutRepositoryReads(string key)
    {
        await using var fixture = new CredentialFixture();
        Assert.IsNull(await fixture.Validator.ValidateAsync(key));
        Assert.AreEqual(0, fixture.Repository.Reads);
    }

    [TestMethod]
    public async Task WellFormedUnknownOrWrongSecretIsRejected()
    {
        await using var fixture = new CredentialFixture();
        var codec = new ManagementCredentialSecretCodec();
        Assert.IsNull(await fixture.Validator.ValidateAsync(codec.Create(Guid.NewGuid())));
        var issued = await fixture.IssueAsync();
        Assert.IsNull(await fixture.Validator.ValidateAsync(codec.Create(issued.Credential.CredentialId)));
        Assert.IsFalse(codec.Verify(issued.ApiKey, "sha256-v1:" + new string('z', 64)));
        Assert.IsFalse(codec.Verify(issued.ApiKey, ""));
    }

    [TestMethod]
    public async Task CapabilitiesExpiryAndCancellationAreExplicit()
    {
        await using var fixture = new CredentialFixture();
        foreach(var capabilities in new string[][] { [], ["*"], ["owner"], ["Errors.Read"], [ManagementOperations.ErrorsRead, ManagementOperations.ErrorsRead] })
            await Assert.ThrowsAsync<ArgumentException>(() => fixture.Provisioning.IssueAsync(capabilities, DateTimeOffset.UtcNow.AddDays(1)));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Provisioning.IssueAsync([ManagementOperations.ErrorsRead], DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Provisioning.IssueAsync([ManagementOperations.ErrorsRead], DateTimeOffset.UtcNow.AddYears(1)));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Validator.ValidateAsync("invalid", canceled.Token));
    }

    [TestMethod]
    public async Task GeneratedSecretsAreUniqueAndRedactedFromSerializationAndAudit()
    {
        var sink = new RecordingSink();
        await using var fixture = new CredentialFixture(customize: services =>
        {
            services.AddSingleton<IAuditTrailSink>(sink);
            services.Configure<ServiceAuditOptions>(o => o.Enabled = true);
        });
        var first = await fixture.IssueAsync();
        var second = await fixture.IssueAsync();
        Assert.AreNotEqual(first.ApiKey, second.ApiKey);
        Assert.AreNotEqual(first.Credential.CredentialId, second.Credential.CredentialId);
        var row = (await fixture.Repository.GetAsync("qurvia", "Production", first.Credential.CredentialId))!;
        await fixture.Provisioning.RotateAsync(row.CredentialId);
        await fixture.Provisioning.RevokeAsync(row.CredentialId);
        var serialized = JsonSerializer.Serialize(new { first, row, audits = sink.Transactions });
        Assert.IsFalse(serialized.Contains(first.ApiKey, StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains(row.SecretHash, StringComparison.Ordinal));
        Assert.IsFalse(first.ToString().Contains(first.ApiKey, StringComparison.Ordinal));
        Assert.IsFalse(row.ToString().Contains(row.SecretHash, StringComparison.Ordinal));
        Assert.HasCount(4, sink.Transactions);
        Assert.IsTrue(sink.Transactions.All(x => x.BeforeJson is null && x.AfterJson is null));
    }

    private sealed class RecordingSink : IAuditTrailSink
    {
        public List<ServiceAuditTransaction> Transactions { get; } = [];
        public Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default)
        { Transactions.Add(transaction); return Task.CompletedTask; }
        public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default) => Task.CompletedTask;
    }
}
