using IBeam.Communications.Abstractions;
using IBeam.Communications.Abstractions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Communications;

[TestClass]
public sealed class SmsInboundComplianceTests
{
    [TestMethod]
    public void Classify_RecognizesCtiaKeywordsCaseInsensitively()
    {
        foreach (var keyword in new[] { "STOP", "stopall", "Unsubscribe", "CANCEL", "end", "QUIT" })
            Assert.AreEqual(SmsInboundKeywordAction.OptOut, SmsInboundKeywordClassifier.Classify($" {keyword} "), keyword);

        foreach (var keyword in new[] { "START", "unstop", "Yes" })
            Assert.AreEqual(SmsInboundKeywordAction.OptIn, SmsInboundKeywordClassifier.Classify(keyword), keyword);

        foreach (var keyword in new[] { "HELP", "info" })
            Assert.AreEqual(SmsInboundKeywordAction.Help, SmsInboundKeywordClassifier.Classify(keyword), keyword);
    }

    [TestMethod]
    public void Classify_IgnoresNonKeywordBodies()
    {
        foreach (var body in new string?[] { null, "", "  ", "123456", "please stop", "STOP PLEASE", "hello" })
            Assert.AreEqual(SmsInboundKeywordAction.None, SmsInboundKeywordClassifier.Classify(body), body ?? "<null>");
    }

    [TestMethod]
    public async Task ProcessAsync_OptOut_RecordsConsentAndReplies()
    {
        var consent = new RecordingConsentStore();
        var sms = new RecordingSmsService();
        var sut = CreateProcessor(consent, sms);

        var result = await sut.ProcessAsync(new SmsInboundMessage("+16145551212", "+16140000000", "STOP"));

        Assert.AreEqual(SmsInboundKeywordAction.OptOut, result.Action);
        Assert.IsTrue(result.ConsentRecorded);
        Assert.IsTrue(result.ReplySent);
        Assert.AreEqual("+16145551212", consent.OptedOut.Single());
        StringAssert.Contains(sms.Sent.Single().Body, "unsubscribed");
        StringAssert.StartsWith(sms.Sent.Single().Body, "Bindry:");
    }

    [TestMethod]
    public async Task ProcessAsync_Start_RecordsOptInWithInboundSource()
    {
        var consent = new RecordingConsentStore();
        var sms = new RecordingSmsService();
        var sut = CreateProcessor(consent, sms);

        var result = await sut.ProcessAsync(new SmsInboundMessage("+16145551212", "+16140000000", "START"));

        Assert.AreEqual(SmsInboundKeywordAction.OptIn, result.Action);
        Assert.AreEqual(("+16145551212", SmsInboundProcessor.OptInSource), consent.OptedIn.Single());
        StringAssert.Contains(sms.Sent.Single().Body, "resubscribed");
    }

    [TestMethod]
    public async Task ProcessAsync_Help_RepliesWithoutTouchingConsent()
    {
        var consent = new RecordingConsentStore();
        var sms = new RecordingSmsService();
        var sut = CreateProcessor(consent, sms);

        var result = await sut.ProcessAsync(new SmsInboundMessage("+16145551212", "+16140000000", "HELP"));

        Assert.AreEqual(SmsInboundKeywordAction.Help, result.Action);
        Assert.IsFalse(result.ConsentRecorded);
        Assert.IsEmpty(consent.OptedOut);
        Assert.IsEmpty(consent.OptedIn);
        StringAssert.Contains(sms.Sent.Single().Body, "support@bindry.ai");
    }

    [TestMethod]
    public async Task ProcessAsync_NonKeyword_DoesNothing()
    {
        var consent = new RecordingConsentStore();
        var sms = new RecordingSmsService();
        var sut = CreateProcessor(consent, sms);

        var result = await sut.ProcessAsync(new SmsInboundMessage("+16145551212", "+16140000000", "123456"));

        Assert.AreEqual(SmsInboundKeywordAction.None, result.Action);
        Assert.IsEmpty(sms.Sent);
    }

    [TestMethod]
    public async Task ProcessAsync_ReplyFailure_IsSwallowedAfterConsentRecorded()
    {
        var consent = new RecordingConsentStore();
        var sms = new RecordingSmsService { ThrowOnSend = true };
        var sut = CreateProcessor(consent, sms);

        var result = await sut.ProcessAsync(new SmsInboundMessage("+16145551212", "+16140000000", "STOP"));

        Assert.IsTrue(result.ConsentRecorded);
        Assert.IsFalse(result.ReplySent);
        Assert.AreEqual("+16145551212", consent.OptedOut.Single());
    }

    [TestMethod]
    public void SmsInboundOptions_RequiresBrandOrFullOverrides()
    {
        Assert.IsFalse(new SmsInboundOptions().Validate());
        Assert.IsTrue(new SmsInboundOptions { BrandName = "Bindry", SupportContact = "support@bindry.ai" }.Validate());
        Assert.IsTrue(new SmsInboundOptions
        {
            OptOutReplyText = "bye",
            OptInReplyText = "hi",
            HelpReplyText = "help"
        }.Validate());
    }

    [TestMethod]
    public void WebhookSharedSecret_FailsClosedAndMatchesExactSecret()
    {
        Assert.IsFalse(WebhookSharedSecret.Matches("anything", ""));
        Assert.IsFalse(WebhookSharedSecret.Matches(null, "secret"));
        Assert.IsFalse(WebhookSharedSecret.Matches("wrong", "secret"));
        Assert.IsTrue(WebhookSharedSecret.Matches("secret", "secret"));
    }

    private static SmsInboundProcessor CreateProcessor(RecordingConsentStore consent, RecordingSmsService sms) =>
        new(
            consent,
            sms,
            Options.Create(new SmsInboundOptions { BrandName = "Bindry", SupportContact = "support@bindry.ai" }),
            NullLogger<SmsInboundProcessor>.Instance);

    private sealed class RecordingConsentStore : ISmsConsentStore
    {
        public List<string> OptedOut { get; } = [];
        public List<(string Phone, string Source)> OptedIn { get; } = [];

        public Task RecordOptOutAsync(string phoneNumber, CancellationToken ct = default)
        {
            OptedOut.Add(phoneNumber);
            return Task.CompletedTask;
        }

        public Task RecordOptInAsync(string phoneNumber, string source, CancellationToken ct = default)
        {
            OptedIn.Add((phoneNumber, source));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSmsService : ISmsService
    {
        public bool ThrowOnSend { get; set; }
        public List<(string To, string Body)> Sent { get; } = [];

        public Task SendAsync(SmsMessage message, IBeam.Communications.Abstractions.Options.SmsOptions? options = null, CancellationToken ct = default)
        {
            if (ThrowOnSend)
                throw new InvalidOperationException("SMS provider unavailable.");

            Sent.Add((message.To.Single(), message.Body));
            return Task.CompletedTask;
        }
    }
}
