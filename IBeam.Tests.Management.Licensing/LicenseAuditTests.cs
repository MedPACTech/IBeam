using System.Security.Claims;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Licensing;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.Licensing;

[TestClass]
public sealed class LicenseAuditTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task NativeReplyAndUnknownOutcomeAuditOnlyVerifiedScopedMetadata(bool lostReply)
    {
        var f = await LicenseStatusTests.Fixture.Create(); var credential = Guid.NewGuid();
        var rows = new List<ManagementAuditEntity>();
        var repository = new Mock<IManagementAuditRepository>();
        repository.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>()))
            .Callback<ManagementAuditEntity, CancellationToken>((row, _) => rows.Add(row)).Returns(Task.CompletedTask);
        var principals = new Mock<IManagementPrincipalProvider>();
        principals.Setup(x => x.GetPrincipal()).Returns(new ClaimsPrincipal(new ClaimsIdentity([
            new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Credential), new(ManagementClaimTypes.CredentialId, credential.ToString("D")),
            new(ManagementClaimTypes.ApplicationKey, "fixture"), new(ManagementClaimTypes.Environment, "Testing") ], "machine")));
        var actor = new Mock<IManagementDelegatedActorContext>();
        actor.SetupGet(x => x.Current).Returns(new ManagementDelegatedActor(f.Context.OrganizationId, f.Context.ActorUserId,
            f.Context.ApplicationId, f.Context.RealmId, credential, "fixture", "Testing", ManagementLicenseOperations.Suspend,
            Guid.NewGuid(), f.Clock.Now.AddMinutes(1), new string('a', 32)));
        var registry = new ManagementOperationRegistry(ManagementLicensingServiceExtensions.LicenseStatusDescriptors);
        var sink = new ManagementAuditSink(repository.Object, principals.Object, new ManagementAuditContext(), Options.Create(new ManagementAuditOptions()),
            Options.Create(new ManagementDiagnosticsOptions { ApplicationKey = "fixture", Environment = "Testing" }), f.Clock, actor.Object, registry);
        var options = new Mock<IOptionsMonitor<ServiceAuditOptions>>();
        options.SetupGet(x => x.CurrentValue).Returns(new ServiceAuditOptions { Enabled = true, FailOnAuditError = true, CaptureBefore = true, CaptureAfter = true });
        var target = new ManagementLicenseTargetService(f.Native, f.Access, new ServiceOperationExecutor(auditTrailSink: sink, auditOptionsMonitor: options.Object));
        f.Store.LoseReply = lostReply;
        var result = await target.ChangeAsync(ManagementLicenseOperations.Suspend, f.Context, f.Context.ResourceId, f.Context.ResourceVersion!);
        Assert.AreEqual(lostReply ? "unknown" : "succeeded", result.Outcome);
        var row = rows.Single();
        Assert.AreEqual(!lostReply, row.Succeeded); Assert.AreEqual(f.Context.ApplicationId, row.ApplicationId);
        Assert.AreEqual(f.Context.OrganizationId, row.OrganizationId); Assert.AreEqual(f.Context.ActorUserId, row.ActorUserId);
        Assert.AreEqual(f.Context.ResourceId, row.SubjectId); Assert.AreEqual(credential, row.CredentialId);
        Assert.DoesNotContain("PROVIDER-PRIVATE-CANARY", JsonSerializer.Serialize(rows));
        Assert.DoesNotContain(f.Context.ResourceVersion!, JsonSerializer.Serialize(rows));
    }
}
