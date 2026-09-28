using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Invites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Services;

/// <summary>
/// IBM-0068. The invite link used to be built from whatever RedirectUrl the caller sent, with no
/// validation and a hardcoded https://localhost:3000 fallback, so anyone who could create an invite could
/// send a genuine app-branded email pointing anywhere.
/// </summary>
[TestClass]
public sealed class TenantInviteLinkPolicyTests
{
    private const string Allowed = "https://app.example.com";

    [TestMethod]
    public void AllowedOrigin_IsUsed()
    {
        var url = Policy(o =>
        {
            o.AllowedOrigins.Add(Allowed);
            o.DefaultAcceptUrl = "https://fallback.example.com/invites/accept";
            o.AcceptPath = "/invites/accept";
        }).ResolveAcceptUrl(Invite($"{Allowed}/invites/accept"));

        Assert.AreEqual($"{Allowed}/invites/accept", url);
    }

    [TestMethod]
    public void DisallowedOrigin_FallsBackAndNeverLinksToIt()
    {
        var url = Policy(o =>
        {
            o.AllowedOrigins.Add(Allowed);
            o.DefaultAcceptUrl = "https://fallback.example.com";
            o.AcceptPath = "/invites/accept";
        }).ResolveAcceptUrl(Invite("https://evil.example/steal"));

        Assert.AreEqual("https://fallback.example.com/invites/accept", url);
        StringAssert.DoesNotMatch(url, new System.Text.RegularExpressions.Regex("evil"));
    }

    [TestMethod]
    public void Reject_RefusesInsteadOfQuietlyChangingTheLink()
    {
        var policy = Policy(o =>
        {
            o.AllowedOrigins.Add(Allowed);
            o.DefaultAcceptUrl = "https://fallback.example.com";
            o.OnDisallowed = TenantInviteDisallowedLinkBehavior.Reject;
        });

        Assert.ThrowsExactly<IdentityValidationException>(() => policy.ResolveAcceptUrl(Invite("https://evil.example")));
    }

    // The lookalike matrix. Every one of these reads as the allowed host to a person skimming an email.
    [TestMethod]
    [DataRow("https://app.example.com@evil.example", DisplayName = "user info before the real host")]
    [DataRow("https://app.example.com.evil.example", DisplayName = "allowed host as a suffix")]
    [DataRow("http://app.example.com", DisplayName = "scheme mismatch")]
    [DataRow("https://app.example.com:8443", DisplayName = "port mismatch")]
    [DataRow("javascript:alert(1)", DisplayName = "non-http scheme")]
    [DataRow("/invites/accept", DisplayName = "relative URL")]
    [DataRow("evil.example", DisplayName = "no scheme")]
    public void LookalikeUrls_AreNotTreatedAsAllowed(string requested)
    {
        var url = Policy(o =>
        {
            o.AllowedOrigins.Add(Allowed);
            o.DefaultAcceptUrl = "https://fallback.example.com";
            o.AcceptPath = "/invites/accept";
        }).ResolveAcceptUrl(Invite(requested));

        Assert.AreEqual("https://fallback.example.com/invites/accept", url);
    }

    [TestMethod]
    public void AcceptPath_DiscardsTheCallersPathOnAnAllowedOrigin()
    {
        // An allowed origin is not a licence to choose any path on it — which matters for a host that
        // serves user-controlled content under some prefix.
        var url = Policy(o =>
        {
            o.AllowedOrigins.Add(Allowed);
            o.DefaultAcceptUrl = "https://fallback.example.com";
            o.AcceptPath = "/invites/accept";
        }).ResolveAcceptUrl(Invite($"{Allowed}/attacker/controlled?q=1"));

        Assert.AreEqual($"{Allowed}/invites/accept", url);
    }

    [TestMethod]
    public void IsAllowedDelegate_CanWidenTheListButNotBypassValidation()
    {
        var options = new TenantInviteLinkOptions
        {
            DefaultAcceptUrl = "https://fallback.example.com",
            AcceptPath = "/invites/accept",
            IsAllowed = (uri, _) => uri.Host.EndsWith(".preview.example.com", StringComparison.OrdinalIgnoreCase)
        };

        var policy = Policy(options);

        Assert.AreEqual(
            "https://pr-12.preview.example.com/invites/accept",
            policy.ResolveAcceptUrl(Invite("https://pr-12.preview.example.com/whatever")));

        // The delegate is only consulted for a structurally valid URL, so it cannot be used to let a
        // user-info trick through even if it would say yes to the host.
        Assert.AreEqual(
            "https://fallback.example.com/invites/accept",
            policy.ResolveAcceptUrl(Invite("https://x.preview.example.com@evil.example")));
    }

    [TestMethod]
    public void AllowAnyOrigin_RestoresTheOldBehaviourDeliberately()
    {
        var options = new TenantInviteLinkOptions { DefaultAcceptUrl = "https://fallback.example.com" };
        options.AllowAnyOrigin();

        Assert.AreEqual("https://anywhere.example/accept", Policy(options).ResolveAcceptUrl(Invite("https://anywhere.example/accept")));
    }

    [TestMethod]
    public void NoDefaultAcceptUrl_FailsWithAMessageNamingTheSetting()
    {
        // Rather than the old silent https://localhost:3000, which was never a working destination outside
        // a developer machine.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => Policy(o => o.AllowedOrigins.Add(Allowed)).ResolveAcceptUrl(Invite("https://evil.example")));

        StringAssert.Contains(ex.Message, "DefaultAcceptUrl");
        StringAssert.Contains(ex.Message, TenantInviteLinkOptions.SectionName);
    }

    [TestMethod]
    public void ConfiguredOrigins_ReplaceRatherThanAppend()
    {
        // The allowlist is the last place to repeat IBM-0072/0073. Bound through the real registration.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIBeamTenantInviteLinks(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TenantInviteLinkOptions.SectionName}:AllowedOrigins:0"] = Allowed,
                [$"{TenantInviteLinkOptions.SectionName}:DefaultAcceptUrl"] = "https://fallback.example.com"
            })
            .Build());

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<TenantInviteLinkOptions>>().Value;

        CollectionAssert.AreEqual(new[] { Allowed }, options.AllowedOrigins);
    }

    [TestMethod]
    public void CodeConfiguration_RunsAfterAndAddsToConfiguredOrigins()
    {
        // The requirement this card exists for: "as long as it can be modified in code not just configuration".
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIBeamTenantInviteLinks(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TenantInviteLinkOptions.SectionName}:AllowedOrigins:0"] = Allowed
            })
            .Build());
        services.AddIBeamTenantInviteLinks(o => o.AllowedOrigins.Add("https://added-in-code.example.com"));

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<TenantInviteLinkOptions>>().Value;

        CollectionAssert.AreEquivalent(
            new[] { Allowed, "https://added-in-code.example.com" },
            options.AllowedOrigins);
    }

    [TestMethod]
    public void ReplacingThePolicyInDi_IsStillPossible()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITenantInviteLinkPolicy, FixedPolicy>();
        services.AddIBeamTenantInviteLinks(o => o.DefaultAcceptUrl = "https://ignored.example.com");

        var resolved = services.BuildServiceProvider().GetRequiredService<ITenantInviteLinkPolicy>();

        Assert.IsInstanceOfType(resolved, typeof(FixedPolicy));
    }

    [TestMethod]
    public void UrlBuilder_AsksThePolicyRatherThanUsingTheCallersRedirect()
    {
        // Gates the wiring, not the policy. Without this, every test above would still pass if
        // DefaultTenantInviteUrlBuilder went back to reading invite.RedirectUrl directly — the policy
        // would be correct and unused.
        var builder = new DefaultTenantInviteUrlBuilder(Policy(o =>
        {
            o.AllowedOrigins.Add(Allowed);
            o.DefaultAcceptUrl = "https://fallback.example.com";
            o.AcceptPath = "/invites/accept";
        }));

        var url = builder.BuildInviteUrl(Invite("https://evil.example/steal"), "real-token");

        Assert.AreEqual("https://fallback.example.com/invites/accept?inviteToken=real-token", url);
    }

    // --- The message factory half of IBM-0068 ---

    [TestMethod]
    public void CallerMetadata_CannotReplaceTheLinkOrTheToken()
    {
        // The second way in, independent of RedirectUrl: metadata was copied over the system keys.
        var invite = Invite(Allowed) with
        {
            Metadata = new Dictionary<string, string>
            {
                ["inviteUrl"] = "https://evil.example",
                ["inviteToken"] = "attacker-token",
                ["tenantId"] = Guid.NewGuid().ToString("D")
            }
        };

        var message = new DefaultTenantInviteMessageFactory()
            .CreateMessage(invite, "real-token", "https://app.example.com/invites/accept?inviteToken=real-token");

        Assert.AreEqual("https://app.example.com/invites/accept?inviteToken=real-token", message.Metadata["inviteUrl"]);
        Assert.AreEqual("real-token", message.Metadata["inviteToken"]);
        Assert.AreEqual(invite.TenantId.ToString("D"), message.Metadata["tenantId"]);
    }

    [TestMethod]
    public void CallerMetadata_CannotSneakInThroughADifferentCase()
    {
        // The old dictionary was case-sensitive, so "InviteUrl" sat alongside "inviteUrl" and both reached
        // the template model — whichever the template read, the caller had a shot at it.
        var invite = Invite(Allowed) with
        {
            Metadata = new Dictionary<string, string>
            {
                ["InviteUrl"] = "https://evil.example",
                ["INVITETOKEN"] = "attacker-token",
                ["Subject"] = "Your account is suspended"
            }
        };

        var message = new DefaultTenantInviteMessageFactory().CreateMessage(invite, "real-token", "https://good.example/accept");

        Assert.AreEqual("https://good.example/accept", message.Metadata["inviteUrl"]);
        Assert.AreEqual("real-token", message.Metadata["inviteToken"]);
        Assert.IsFalse(message.Metadata.ContainsKey("Subject"), "A reserved template key must not reach the model from caller metadata.");
        Assert.AreEqual(1, message.Metadata.Keys.Count(k => k.Equals("inviteUrl", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CallerMetadata_ThatIsNotReservedStillPassesThrough()
    {
        // The feature is not "drop caller metadata" — apps use it for their own template values.
        var invite = Invite(Allowed) with { Metadata = new Dictionary<string, string> { ["referrer"] = "spring-campaign" } };

        var message = new DefaultTenantInviteMessageFactory().CreateMessage(invite, "t", "https://good.example/accept");

        Assert.AreEqual("spring-campaign", message.Metadata["referrer"]);
    }

    [TestMethod]
    public void ReservedKeys_AreDetectableForRejectionAtCreateTime()
    {
        var present = TenantInviteReservedMetadataKeys.PresentIn(new Dictionary<string, string>
        {
            ["InviteUrl"] = "x",
            ["referrer"] = "y"
        });

        CollectionAssert.AreEqual(new[] { "InviteUrl" }, present.ToArray());
    }

    private static TenantInviteRecord Invite(string? redirectUrl) => new(
        InviteId: Guid.NewGuid(),
        TenantId: Guid.NewGuid(),
        DestinationType: TenantInviteDestinationTypes.Email,
        NormalizedDestination: "invitee@example.com",
        TokenHash: "hash",
        Status: TenantInviteStatuses.Pending,
        CreatedUtc: DateTimeOffset.UtcNow,
        InvitedByUserId: Guid.NewGuid(),
        ExpiresUtc: DateTimeOffset.UtcNow.AddDays(7),
        RedirectUrl: redirectUrl);

    private static DefaultTenantInviteLinkPolicy Policy(Action<TenantInviteLinkOptions> configure)
    {
        var options = new TenantInviteLinkOptions();
        configure(options);
        return Policy(options);
    }

    private static DefaultTenantInviteLinkPolicy Policy(TenantInviteLinkOptions options) =>
        new(Options.Create(options), NullLogger<DefaultTenantInviteLinkPolicy>.Instance);

    private sealed class FixedPolicy : ITenantInviteLinkPolicy
    {
        public string ResolveAcceptUrl(TenantInviteRecord invite) => "https://fixed.example.com/accept";
    }
}
