using IBeam.Communications.Abstractions;
using IBeam.Communications.Sms.AzureCommunications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IBeam.Tests.Communications.Sms.AzureCommunications;

[TestClass]
public sealed class AzureEventGridSmsInboundHandlerTests
{
    [TestMethod]
    public async Task HandleAsync_SubscriptionValidation_EchoesValidationCode()
    {
        var sut = CreateSut(out _);

        var result = await sut.HandleAsync("""
            [{"eventType":"Microsoft.EventGrid.SubscriptionValidationEvent","data":{"validationCode":"code-123"}}]
            """);

        Assert.IsNotNull(result.ValidationResponse);
        Assert.AreEqual("code-123", result.ValidationResponse.ValidationResponse);
        Assert.IsEmpty(result.Processed);
    }

    [TestMethod]
    public async Task HandleAsync_SmsReceived_RoutesEachEventThroughTheProcessor()
    {
        var sut = CreateSut(out var processor);

        var result = await sut.HandleAsync("""
            [
              {"eventType":"Microsoft.Communication.SMSReceived","data":{"from":"+16145551212","to":"+16140000000","message":"STOP","receivedTimestamp":"2026-08-30T12:00:00Z"}},
              {"eventType":"Microsoft.Communication.SMSReceived","data":{"from":"+16145559999","to":"+16140000000","message":"hello"}}
            ]
            """);

        Assert.IsNull(result.ValidationResponse);
        Assert.HasCount(2, result.Processed);
        Assert.HasCount(2, processor.Messages);
        Assert.AreEqual("+16145551212", processor.Messages[0].From);
        Assert.AreEqual("STOP", processor.Messages[0].Body);
        Assert.AreEqual(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), processor.Messages[0].ReceivedUtc);
    }

    [TestMethod]
    public async Task HandleAsync_IgnoresUnknownEventTypesAndEmptyBodies()
    {
        var sut = CreateSut(out var processor);

        var unknown = await sut.HandleAsync("""
            [{"eventType":"Microsoft.Communication.SMSDeliveryReportReceived","data":{"from":"+1"}}]
            """);
        var empty = await sut.HandleAsync("");

        Assert.IsEmpty(unknown.Processed);
        Assert.IsEmpty(empty.Processed);
        Assert.IsEmpty(processor.Messages);
    }

    [TestMethod]
    public async Task HandleAsync_FailedDeliveryReport_LogsErrorInsteadOfStayingSilent()
    {
        var logger = new CapturingLogger();
        var sut = new AzureEventGridSmsInboundHandler(new RecordingProcessor(), logger);

        await sut.HandleAsync("""
            [{"eventType":"Microsoft.Communication.SMSDeliveryReportReceived","data":{"to":"+16145551212","messageId":"msg-1","deliveryStatus":"Failed","deliveryStatusDetails":"Carrier rejected"}}]
            """);

        var entry = logger.Entries.Single();
        Assert.AreEqual(LogLevel.Error, entry.Level);
        StringAssert.Contains(entry.Message, "delivery failed");
        StringAssert.Contains(entry.Message, "msg-1");
        StringAssert.Contains(entry.Message, "Carrier rejected");
    }

    [TestMethod]
    public async Task HandleAsync_SuccessfulDeliveryReport_LogsInformation()
    {
        var logger = new CapturingLogger();
        var sut = new AzureEventGridSmsInboundHandler(new RecordingProcessor(), logger);

        await sut.HandleAsync("""
            [{"eventType":"Microsoft.Communication.SMSDeliveryReportReceived","data":{"to":"+16145551212","messageId":"msg-1","deliveryStatus":"Delivered"}}]
            """);

        var entry = logger.Entries.Single();
        Assert.AreEqual(LogLevel.Information, entry.Level);
        StringAssert.Contains(entry.Message, "delivered");
    }

    private static AzureEventGridSmsInboundHandler CreateSut(out RecordingProcessor processor)
    {
        processor = new RecordingProcessor();
        return new AzureEventGridSmsInboundHandler(processor, NullLogger<AzureEventGridSmsInboundHandler>.Instance);
    }

    private sealed class CapturingLogger : ILogger<AzureEventGridSmsInboundHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class RecordingProcessor : ISmsInboundProcessor
    {
        public List<SmsInboundMessage> Messages { get; } = [];

        public Task<SmsInboundProcessingResult> ProcessAsync(SmsInboundMessage message, CancellationToken ct = default)
        {
            Messages.Add(message);
            return Task.FromResult(new SmsInboundProcessingResult(
                SmsInboundKeywordClassifier.Classify(message.Body),
                false,
                false));
        }
    }
}
