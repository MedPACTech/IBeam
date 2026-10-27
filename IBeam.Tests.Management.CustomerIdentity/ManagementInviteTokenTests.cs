using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using IBeam.Management.CustomerIdentity;
using Moq;

namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class ManagementInviteTokenTests
{
    [TestMethod]
    public async Task ProvisioningCanReadAnExistingPaddedTokenWithoutRewritingOrResendingIt()
    {
        var fixture = new ProvisioningFixture();
        var first = await fixture.Service.ProvisionAsync(fixture.Tenant, fixture.Request, "legacy-provision");
        var row = fixture.Journal.Rows.Values.Single();
        var binding = JsonSerializer.SerializeToUtf8Bytes(new { row.Scope, row.TenantId, row.InviteId,
            row.OriginalOperationId, row.OrganizationId, row.ApplicationId, row.CreatedByUserId, row.CreatedUtc, row.ExpiresUtc });
        var encrypted = LegacyRecovery(row.EncryptedInput, fixture.Data.Operation.Options.FingerprintKey,
            "ibeam-customer-provisioning-v1:recovery", binding, out _);
        fixture.Journal.Rows[(row.Scope, row.TenantId, row.InviteId)] = row with { EncryptedInput = encrypted };

        var repeat = await fixture.Service.ProvisionAsync(fixture.Tenant, fixture.Request, "legacy-provision");

        Assert.AreEqual(first.Operation, repeat.Operation);
        Assert.AreEqual(first.Invitation!.InviteId, repeat.Invitation!.InviteId);
        Assert.AreEqual(first.Status, repeat.Status);
        Assert.AreEqual(1, fixture.Data.Sends);
        Assert.AreEqual(encrypted, fixture.Journal.Rows.Values.Single().EncryptedInput);
    }

    [TestMethod]
    public async Task OnboardingExplicitRetryDeliversAnExistingPaddedTokenVerbatim()
    {
        var fixture = new OnboardingFixture { FailDelivery = true };
        var first = await fixture.Service.CreateAsync(fixture.Request, "legacy-onboard");
        var row = fixture.Jobs.Rows.Values.Single();
        var binding = JsonSerializer.SerializeToUtf8Bytes(new { row.Scope, row.ApplicationKey, row.Environment,
            row.OperationId, row.OnboardingId, row.InviteId, row.CreatedByUserId, row.CreatedUtc, row.ExpiresUtc });
        var encrypted = LegacyRecovery(row.EncryptedInput, fixture.Operation.Options.FingerprintKey,
            "ibeam-onboarding-recovery-v1", binding, out var legacyToken);
        fixture.Jobs.Rows[(row.Scope, row.OnboardingId)] = row with { EncryptedInput = encrypted };
        fixture.FailDelivery = false;
        fixture.Operation.Actor = fixture.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.OnboardingRetry };

        var retried = await fixture.Service.RetryAsync(first.OnboardingId, "legacy-retry");

        Assert.AreEqual("sent", retried.DeliveryStatus);
        Mock.Get(fixture.External).Verify(x => x.DeliverAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            legacyToken, It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual(encrypted, fixture.Jobs.Rows.Values.Single().EncryptedInput);
    }

    // Construct the authenticated v1 recovery format used by 2.16.0 with a padded token.
    // This exercises public recovery workflows rather than only a token-format predicate.
    private static string LegacyRecovery(string ciphertext, string fingerprintKey, string purpose, byte[] binding, out string token)
    {
        var bytes = Convert.FromBase64String(ciphertext);
        var key = HMACSHA256.HashData(Convert.FromBase64String(fingerprintKey), Encoding.UTF8.GetBytes(purpose));
        var plaintext = new byte[bytes.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plaintext, binding);
        var input = JsonNode.Parse(plaintext)!.AsObject();
        token = Convert.ToBase64String(Enumerable.Repeat((byte)251, 32).ToArray());
        input["Token"] = token;
        plaintext = JsonSerializer.SerializeToUtf8Bytes(input);
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var encrypted = new byte[plaintext.Length];
        aes.Encrypt(nonce, plaintext, encrypted, tag, binding);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(encrypted).ToArray());
    }

    [TestMethod]
    [DataRow("resend")]
    [DataRow("provisioning")]
    [DataRow("onboarding")]
    public async Task GeneratedInvitationTokensAreSafeForUrlPaths(string path)
    {
        var generated = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            string token;
            if (path == "resend")
            {
                var fixture = new InvitationLifecycleFixture();
                await fixture.Service.ResendAsync(fixture.Tenant, fixture.Id, "url-safe-resend", fixture.Row.Version!);
                token = fixture.LastToken!;
                Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), fixture.Row.TokenHash);
            }
            else if (path == "provisioning")
            {
                var fixture = new ProvisioningFixture();
                await fixture.Service.ProvisionAsync(fixture.Tenant, fixture.Request, "url-safe-provision");
                token = fixture.Data.LastRequest!.Token;
            }
            else
            {
                var fixture = new OnboardingFixture();
                await fixture.Service.CreateAsync(fixture.Request, "url-safe-onboard");
                token = fixture.LastRequest!.Token;
            }

            Assert.IsTrue(Regex.IsMatch(token, "^[A-Za-z0-9_-]+$"), "Invite token must be unpadded base64url.");
            Assert.AreEqual(43, token.Length);
            Assert.AreEqual(32, Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=").Length);
            Assert.IsTrue(generated.Add(token), "Independent invitations must not reuse a token.");
        }
    }
}
