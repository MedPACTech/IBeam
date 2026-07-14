using IBeam.Api.Abstractions;
using IBeam.Communications.Abstractions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Otp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class IdentityCommunicationAdapterTests
{
    [TestMethod]
    public async Task SendAsync_EmailWithTemplate_UsesTemplatedEmailService()
    {
        IReadOnlyCollection<string>? to = null;
        string? subject = null;
        string? templateName = null;
        object? model = null;

        var templated = new Mock<ITemplatedEmailService>(MockBehavior.Strict);
        templated.Setup(x => x.SendTemplatedEmailAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>, string, string, object?, EmailOptions?, CancellationToken>((t, s, tn, m, _, _) =>
            {
                to = t;
                subject = s;
                templateName = tn;
                model = m;
            })
            .Returns(Task.CompletedTask);

        var email = new Mock<IEmailService>(MockBehavior.Strict);

        var services = new ServiceCollection();
        services.AddSingleton(templated.Object);
        services.AddSingleton(email.Object);
        services.AddSingleton<IOptions<IdentityEmailTemplateOptions>>(Options.Create(new IdentityEmailTemplateOptions
        {
            Enabled = true,
            ExpirationDisplay = ExpirationDisplayMode.MinutesRemaining,
            PurposeTemplates = new Dictionary<string, IdentityEmailTemplateDefinition>
            {
                [SenderPurpose.LoginMfa.ToString()] = new()
                {
                    TemplateName = "OtpLogin",
                    Subject = "Your login code"
                }
            }
        }));

        var adapter = new IdentityCommunicationAdapter(services.BuildServiceProvider());

        await adapter.SendAsync(new IdentitySenderMessage
        {
            Channel = SenderChannel.Email,
            Destination = "abram.cookson@outlook.com",
            Purpose = SenderPurpose.LoginMfa,
            Code = "123456",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(7)
        });

        Assert.IsNotNull(to);
        Assert.AreEqual("abram.cookson@outlook.com", to!.Single());
        Assert.AreEqual("Your login code", subject);
        Assert.AreEqual("OtpLogin", templateName);

        var modelDict = model as Dictionary<string, object?>;
        Assert.IsNotNull(modelDict);
        Assert.IsTrue(modelDict!.ContainsKey("ExpiresInMinutes"));
        Assert.IsTrue(modelDict.ContainsKey("ExpiresAt"));
        Assert.IsInstanceOfType(modelDict["ExpiresAt"], typeof(int));
        templated.VerifyAll();
    }

    [TestMethod]
    public async Task SendAsync_EmailTemplateMissingAndFallbackEnabled_UsesPlainEmailService()
    {
        var templated = new Mock<ITemplatedEmailService>(MockBehavior.Strict);
        templated.Setup(x => x.SendTemplatedEmailAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Template not found."));

        var email = new Mock<IEmailService>(MockBehavior.Strict);
        email.Setup(x => x.SendAsync(
                "abram.cookson@outlook.com",
                It.Is<string>(s => !string.IsNullOrWhiteSpace(s)),
                null,
                "Your code is: 123456",
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(templated.Object);
        services.AddSingleton(email.Object);
        services.AddSingleton<IOptions<IdentityEmailTemplateOptions>>(Options.Create(new IdentityEmailTemplateOptions
        {
            Enabled = true,
            FallbackToPlainIfMissingTemplate = true,
            PurposeTemplates = new Dictionary<string, IdentityEmailTemplateDefinition>
            {
                [SenderPurpose.PasswordReset.ToString()] = new()
                {
                    TemplateName = "PasswordReset"
                }
            }
        }));

        var adapter = new IdentityCommunicationAdapter(services.BuildServiceProvider());

        await adapter.SendAsync(new IdentitySenderMessage
        {
            Channel = SenderChannel.Email,
            Destination = "abram.cookson@outlook.com",
            Purpose = SenderPurpose.PasswordReset,
            Code = "123456"
        });

        email.VerifyAll();
    }

    [TestMethod]
    public async Task SendAsync_SmsWithoutSmsService_PersistsSystemErrorAndThrowsConfigurationException()
    {
        var services = new ServiceCollection();
        var sink = new RecordingApiErrorSink();
        services.AddSingleton<IApiErrorSink>(sink);
        var adapter = new IdentityCommunicationAdapter(services.BuildServiceProvider());

        await AssertThrowsAsync<SmsConfigurationException>(() =>
            adapter.SendAsync(new IdentitySenderMessage
            {
                Channel = SenderChannel.Sms,
                Destination = "+15555550123",
                Body = "test"
            }));

        var error = AssertSingleSystemError(sink);
        Assert.AreEqual("IdentityCommunicationAdapter", error.Source);
        Assert.AreEqual("IdentityCommunication/Sms", error.Path);
        StringAssert.Contains(error.Message, "SMS service is not configured.");
    }

    [TestMethod]
    public async Task SendAsync_EmailProviderFailure_PersistsSystemErrorAndRethrows()
    {
        var email = new Mock<IEmailService>(MockBehavior.Strict);
        email.Setup(x => x.SendAsync(
                "abram.cookson@outlook.com",
                It.IsAny<string>(),
                null,
                "body",
                null,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmailProviderException("TestEmail", "Provider failed.", true, "503"));

        var sink = new RecordingApiErrorSink();
        var services = new ServiceCollection();
        services.AddSingleton(email.Object);
        services.AddSingleton<IApiErrorSink>(sink);

        var adapter = new IdentityCommunicationAdapter(services.BuildServiceProvider());

        await AssertThrowsAsync<EmailProviderException>(() =>
            adapter.SendAsync(new IdentitySenderMessage
            {
                Channel = SenderChannel.Email,
                Destination = "abram.cookson@outlook.com",
                Body = "body",
                Purpose = SenderPurpose.PasswordReset,
                Metadata = new Dictionary<string, object> { ["traceId"] = "trace-123" }
            }));

        var error = AssertSingleSystemError(sink);
        Assert.AreEqual("IdentityCommunicationAdapter", error.Source);
        Assert.AreEqual("IdentityCommunication/Email", error.Path);
        Assert.AreEqual("SEND", error.Method);
        Assert.AreEqual("trace-123", error.TraceId);
        StringAssert.Contains(error.Message, "Channel=Email");
        StringAssert.Contains(error.Message, "Provider failed.");
        StringAssert.Contains(error.Exception, nameof(EmailProviderException));
        email.VerifyAll();
    }

    [TestMethod]
    public async Task SendAsync_EmailServiceResolutionFailure_PersistsSystemErrorAndRethrows()
    {
        var sink = new RecordingApiErrorSink();
        var services = new ServiceCollection();
        services.AddSingleton<IEmailService>(_ => throw new EmailConfigurationException("Bad email connection string."));
        services.AddSingleton<IApiErrorSink>(sink);

        var adapter = new IdentityCommunicationAdapter(services.BuildServiceProvider());

        await AssertThrowsAsync<EmailConfigurationException>(() =>
            adapter.SendAsync(new IdentitySenderMessage
            {
                Channel = SenderChannel.Email,
                Destination = "abram.cookson@outlook.com",
                Body = "body"
            }));

        var error = AssertSingleSystemError(sink);
        Assert.AreEqual("IdentityCommunication/Email", error.Path);
        StringAssert.Contains(error.Message, "Bad email connection string.");
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected exception {typeof(TException).Name} was not thrown.");
            throw new InvalidOperationException("Unreachable");
        }
        catch (TException ex)
        {
            return ex;
        }
    }

    private static ApiErrorRecord AssertSingleSystemError(RecordingApiErrorSink sink)
    {
        Assert.AreEqual(1, sink.Errors.Count);
        return sink.Errors.Single();
    }

    private sealed class RecordingApiErrorSink : IApiErrorSink
    {
        public List<ApiErrorRecord> Errors { get; } = new();

        public Task SaveAsync(ApiErrorRecord error, CancellationToken cancellationToken = default)
        {
            Errors.Add(error);
            return Task.CompletedTask;
        }
    }
}
