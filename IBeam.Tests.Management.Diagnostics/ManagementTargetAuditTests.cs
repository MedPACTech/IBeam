using System.Net;
using System.Text.Json;
using IBeam.Management;
namespace IBeam.Tests.Management.Diagnostics;
[TestClass]
public sealed class ManagementTargetAuditTests
{
    [TestMethod]
    public async Task AuthenticatedTargetRecordsMachineAndCorrelationWithoutTrustingHumanHeaders()
    {
        await using var f = await SystemErrorApiFixture.CreateAsync(managementAudit: true, installLogs: true);
        var key = await f.IssueAsync(ManagementOperations.ErrorsRead, ManagementOperations.LogsRead, ManagementOperations.DiagnosticsInfo);
        var correlation = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/management-diagnostics/system-errors");
        request.Headers.Add("X-IBeam-Management-Key", key.ApiKey);
        request.Headers.Add("X-IBeam-Management-Correlation", correlation);
        request.Headers.Add("X-IBeam-Management-Actor", SystemErrorApiFixture.Sensitive);
        Assert.AreEqual(HttpStatusCode.OK, (await f.Client.SendAsync(request)).StatusCode);
        var audits = f.ManagementAudits.Rows.Where(x => x.Operation == ManagementOperations.ErrorsRead).ToArray();
        Assert.IsTrue(audits.Length > 0);
        Assert.IsTrue(audits.All(x => x.CredentialId == key.Credential.CredentialId && x.ActorUserId is null && x.ApplicationKey == "qurvia" && x.Environment == "Production" && x.CorrelationId == correlation));
        var text = JsonSerializer.Serialize(f.ManagementAudits.Rows);
        Assert.DoesNotContain(SystemErrorApiFixture.Sensitive, text); Assert.DoesNotContain(key.ApiKey, text);
        using var duplicate = new HttpRequestMessage(HttpMethod.Get, "/api/management-diagnostics/info");
        duplicate.Headers.Add("X-IBeam-Management-Key", key.ApiKey); duplicate.Headers.Add("X-IBeam-Management-Correlation", new[] { correlation, correlation });
        Assert.AreEqual(HttpStatusCode.OK, (await f.Client.SendAsync(duplicate)).StatusCode);
        Assert.IsTrue(f.ManagementAudits.Rows.Any(x => x.Operation == ManagementOperations.DiagnosticsInfo && x.CorrelationId != correlation));
    }
}
