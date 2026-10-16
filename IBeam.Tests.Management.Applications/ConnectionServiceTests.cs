using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class ConnectionServiceTests
{
    [TestMethod]
    public async Task VerifiedRotationStoresOnlyReferencesAndReplacesActivePointer()
    {
        using var f = new ConnectionFixture(); var app = await f.CreateAsync();
        var first = await f.Service.PutAsync(app.ApplicationId, new() { ApiKey = ConnectionFixture.Key, ExpectedVersion = app.Version });
        Assert.HasCount(1, f.Credentials.Rows);
        var firstId = f.Credentials.Rows[0].CredentialId;
        var second = await f.Service.PutAsync(app.ApplicationId, new() { ApiKey = ConnectionFixture.Key + "new", ExpectedVersion = first.Version });
        Assert.AreNotEqual(first.Version, second.Version);
        Assert.AreNotEqual(firstId, f.Apps.Repository.Rows[(f.Apps.OrganizationId, app.ApplicationId)].ActiveCredentialId);
        Assert.HasCount(2, f.Secrets.Rows);
        var test = await f.Service.TestAsync(app.ApplicationId);
        CollectionAssert.AreEqual(new[] { ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead }, test.Capabilities.ToArray());
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Apps.Repository.Rows.Values));
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Credentials.Rows));
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Apps.Audit));
        Assert.DoesNotContain("SecretReference", JsonSerializer.Serialize(f.Credentials.Rows));
    }
    [TestMethod]
    [DataRow("target")]
    [DataRow("environment")]
    [DataRow("protocol")]
    [DataRow("capability")]
    [DataRow("outage")]
    [DataRow("timeout")]
    public async Task FailedRotationPreservesWorkingConnectionAndSanitizesFailures(string failure)
    {
        using var f = new ConnectionFixture(); var app = await f.CreateAsync();
        var first = await f.Service.PutAsync(app.ApplicationId, new() { ApiKey = ConnectionFixture.Key, ExpectedVersion = app.Version });
        var old = f.Apps.Repository.Rows[(f.Apps.OrganizationId, app.ApplicationId)].ActiveCredentialId;
        if (failure == "target") f.Probe.Info = f.Probe.Info with { ApplicationKey = "bindry" };
        if (failure == "environment") f.Probe.Info = f.Probe.Info with { Environment = "Test" };
        if (failure == "protocol") f.Probe.Info = f.Probe.Info with { ProtocolVersion = 99 };
        if (failure == "capability") f.Probe.Info = f.Probe.Info with { Capabilities = ["god-access"] };
        if (failure == "outage") f.Probe.Failure = new Exception(ConnectionFixture.Key);
        if (failure == "timeout") f.Probe.During = ct => Task.Delay(Timeout.Infinite, ct);
        Exception? caught = null;
        try { await f.Service.PutAsync(app.ApplicationId, new() { ApiKey = ConnectionFixture.Key + "pending", ExpectedVersion = first.Version }); }
        catch (Exception ex) { caught = ex; }
        Assert.IsNotNull(caught);
        Assert.AreEqual(old, f.Apps.Repository.Rows[(f.Apps.OrganizationId, app.ApplicationId)].ActiveCredentialId);
        Assert.HasCount(1, f.Credentials.Rows);
        Assert.DoesNotContain(ConnectionFixture.Key, caught.ToString());
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Apps.Audit));
    }
    [TestMethod]
    public async Task ConcurrentDisableOrOriginChangeCannotBeUndoneByPendingActivation()
    {
        using var f = new ConnectionFixture(); var app = await f.CreateAsync();
        f.Probe.During = async _ => { await f.Apps.Service.UpdateAsync(app.ApplicationId, new()
            { Name = app.Name, BaseUrl = "https://new.qurvia.app", IsEnabled = false, ExpectedVersion = app.Version }); };
        await Assert.ThrowsAsync<ManagementApplicationConflictException>(() => f.Service.PutAsync(app.ApplicationId,
            new() { ApiKey = ConnectionFixture.Key, ExpectedVersion = app.Version }));
        var final = await f.Apps.Service.GetAsync(app.ApplicationId);
        Assert.IsFalse(final.IsEnabled); Assert.IsFalse(final.HasConnection);
    }
    [TestMethod]
    public async Task ReaderCrossOrganizationDisabledAndStaleVersionAreDeniedBeforeSecretWrites()
    {
        using var f = new ConnectionFixture(); var app = await f.CreateAsync();
        var request = new UpdateManagementConnection { ApiKey = ConnectionFixture.Key, ExpectedVersion = app.Version };
        f.Apps.Admin = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.PutAsync(app.ApplicationId, request));
        f.Apps.Admin = true; var org = f.Apps.OrganizationId; f.Apps.OrganizationId = Guid.NewGuid();
        await Assert.ThrowsAsync<ManagementApplicationNotFoundException>(() => f.Service.PutAsync(app.ApplicationId, request));
        f.Apps.OrganizationId = org;
        await Assert.ThrowsAsync<ManagementApplicationConflictException>(() => f.Service.PutAsync(app.ApplicationId,
            new() { ApiKey = ConnectionFixture.Key, ExpectedVersion = "old" }));
        await f.Apps.Service.UpdateAsync(app.ApplicationId, new() { Name = app.Name, BaseUrl = app.BaseUrl, IsEnabled = false, ExpectedVersion = app.Version });
        var current = await f.Apps.Service.GetAsync(app.ApplicationId);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.PutAsync(app.ApplicationId,
            new() { ApiKey = ConnectionFixture.Key, ExpectedVersion = current.Version }));
        Assert.HasCount(0, f.Secrets.Rows); Assert.AreEqual(0, f.Probe.Calls);
    }
    [TestMethod]
    public async Task EnvironmentModeReadsFixedScopedBindingsAndNeverAcceptsPortalSubmissions()
    {
        var variable = "IBEAM_FIXTURE_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, ConnectionFixture.Key);
        try
        {
            var bindings = new ManagementEnvironmentSecretOptions();
            var store = new ManagementEnvironmentSecretStore(Options.Create(bindings));
            using var f = new ConnectionFixture(store); var app = await f.CreateAsync();
            var binding = new ManagementEnvironmentSecretBinding(f.Apps.OrganizationId, app.ApplicationId, "fixed-qurvia", variable);
            bindings.Bindings.Add(binding); f.Bindings.Bindings.Add(binding);
            await Assert.ThrowsAsync<ManagementConnectionInvalidException>(() => f.Service.PutAsync(app.ApplicationId,
                new() { ApiKey = ConnectionFixture.Key + "bad", ExpectedVersion = app.Version }));
            Assert.AreEqual(ConnectionFixture.Key, Environment.GetEnvironmentVariable(variable));
            await f.Service.ActivateEnvironmentReferenceAsync(app.ApplicationId, app.Version);
            await f.Service.TestAsync(app.ApplicationId);
            await Assert.ThrowsAsync<ManagementSecretStoreUnavailableException>(() => store.ReadAsync(new(Guid.NewGuid(), app.ApplicationId), binding.Reference));
            await Assert.ThrowsAsync<ManagementSecretStoreUnavailableException>(() => store.ReadAsync(new(f.Apps.OrganizationId, app.ApplicationId), variable));
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }
    [TestMethod]
    public async Task RequestCancellationIsPreservedAndKeyModelsCannotSerializeSecrets()
    {
        using var f = new ConnectionFixture(); var app = await f.CreateAsync();
        f.Probe.During = ct => Task.Delay(Timeout.Infinite, ct);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Service.PutAsync(app.ApplicationId,
            new() { ApiKey = ConnectionFixture.Key, ExpectedVersion = app.Version }, cancel.Token));
        Assert.IsFalse((await f.Apps.Service.GetAsync(app.ApplicationId)).HasConnection);
        var dto = JsonSerializer.Deserialize<UpdateManagementConnection>("{\"ApiKey\":\"" + ConnectionFixture.Key + "\"}")!;
        Assert.AreEqual(ConnectionFixture.Key, dto.ApiKey);
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(dto));
        Assert.DoesNotContain(ConnectionFixture.Key, dto.ToString());
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(new ManagementSecretValue(ConnectionFixture.Key)));
    }
}
