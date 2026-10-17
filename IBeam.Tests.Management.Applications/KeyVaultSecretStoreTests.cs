using Azure;
using Azure.Security.KeyVault.Secrets;
using IBeam.Management;
using IBeam.Management.Secrets.AzureKeyVault;
using Moq;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class KeyVaultSecretStoreTests
{
    [TestMethod]
    public async Task VersionedSecretsAreScopedAndProviderErrorsAreRedacted()
    {
        var rows = new Dictionary<string, KeyVaultSecret>();
        var client = new Mock<SecretClient>();
        client.Setup(x => x.SetSecretAsync(It.IsAny<KeyVaultSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KeyVaultSecret secret, CancellationToken _) =>
            {
                var properties = SecretModelFactory.SecretProperties(name: secret.Name, version: "fixtureversion1");
                foreach (var tag in secret.Properties.Tags) properties.Tags[tag.Key] = tag.Value;
                var saved = SecretModelFactory.KeyVaultSecret(properties, secret.Value);
                rows.Add(secret.Name, saved);
                return Response.FromValue(saved, Mock.Of<Response>());
            });
        client.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, string version, CancellationToken _) => Response.FromValue(rows[name], Mock.Of<Response>()));
        var store = new AzureKeyVaultManagementSecretStore(client.Object);
        var scope = new ManagementSecretScope(Guid.NewGuid(), Guid.NewGuid());
        var reference = await store.CreateAsync(scope, new(ConnectionFixture.Key));
        Assert.DoesNotContain(ConnectionFixture.Key, reference);
        Assert.AreEqual(ConnectionFixture.Key, (await store.ReadAsync(scope, reference)).Value);
        await Assert.ThrowsAsync<ManagementSecretStoreUnavailableException>(() => store.ReadAsync(new(Guid.NewGuid(), scope.ApplicationId), reference));
        await Assert.ThrowsAsync<ManagementSecretStoreUnavailableException>(() => store.ReadAsync(new(scope.OrganizationId, Guid.NewGuid()), reference));
        await Assert.ThrowsAsync<ManagementSecretStoreUnavailableException>(() => store.ReadAsync(scope, "https://evil.example/secret"));
        await Assert.ThrowsAsync<ManagementSecretStoreUnavailableException>(() => store.ReadAsync(scope, reference[..reference.LastIndexOf('/')]));
        rows.Values.Single().Properties.Tags["application"] = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<ManagementSecretStoreUnavailableException>(() => store.ReadAsync(scope, reference));
        client.Setup(x => x.SetSecretAsync(It.IsAny<KeyVaultSecret>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, ConnectionFixture.Key));
        var error = await Assert.ThrowsAsync<ManagementSecretStoreUnavailableException>(() => store.CreateAsync(scope, new(ConnectionFixture.Key)));
        Assert.DoesNotContain(ConnectionFixture.Key, error.ToString());
        Assert.IsNull(error.InnerException);
    }
}
