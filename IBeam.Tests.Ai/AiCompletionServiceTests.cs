using IBeam.Ai.Completions;
using IBeam.Ai.Services.Completions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Ai;

[TestClass]
public sealed class AiCompletionServiceTests
{
    [TestMethod]
    public void AiOptions_ProfileReferencingUnknownProviderFailsValidation()
    {
        var options = new AiOptions
        {
            Providers = { ["anthropic"] = new AiProviderOptions { Type = "anthropic" } },
            Profiles = { ["drafting"] = new AiProfileOptions { Provider = "missing", Model = "claude-opus-5" } }
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => options.Validate());
    }

    [TestMethod]
    public void AiOptions_BlankApiKeyIsValidSoEnvironmentsWithoutCredentialsBoot()
    {
        var options = new AiOptions
        {
            DefaultProfile = "drafting",
            Providers = { ["anthropic"] = new AiProviderOptions { Type = "anthropic", ApiKey = "" } },
            Profiles = { ["drafting"] = new AiProfileOptions { Provider = "anthropic", Model = "claude-opus-5" } }
        };

        Assert.IsTrue(options.Validate());
    }

    [TestMethod]
    public async Task CompleteAsync_WithBlankApiKey_ReturnsNotConfiguredInsteadOfThrowing()
    {
        var provider = new FakeProvider();
        var sut = CreateSut(provider, apiKey: "");

        var result = await sut.CompleteAsync(new AiCompletionRequest { Messages = [AiMessage.User("hi")] });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AiCompletionStatus.NotConfigured, result.Status);
        Assert.AreEqual(0, provider.Calls.Count);
    }

    [TestMethod]
    public async Task CompleteAsync_WithNoAdapterForProviderType_ReturnsNotConfigured()
    {
        var sut = new AiCompletionService(
            Options.Create(TwoProfileOptions(apiKey: "key")),
            providers: [],
            NullLogger<AiCompletionService>.Instance);

        var result = await sut.CompleteAsync(new AiCompletionRequest { Messages = [AiMessage.User("hi")] });

        Assert.AreEqual(AiCompletionStatus.NotConfigured, result.Status);
    }

    [TestMethod]
    public async Task CompleteAsync_RetriesTransientFailuresThenSucceeds()
    {
        var provider = new FakeProvider(
            AiCompletionResult.Fail(AiCompletionStatus.Unavailable, "rate limited"),
            AiCompletionResult.Fail(AiCompletionStatus.Unavailable, "rate limited"),
            AiCompletionResult.Ok("done", new AiUsage(10, 5, 0, 0)));
        var sut = CreateSut(provider);

        var result = await sut.CompleteAsync(new AiCompletionRequest { Messages = [AiMessage.User("hi")] });

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(3, provider.Calls.Count);
    }

    [TestMethod]
    public async Task CompleteAsync_NeverRetriesRefusals()
    {
        var provider = new FakeProvider(
            AiCompletionResult.Fail(AiCompletionStatus.Refused, "declined"),
            AiCompletionResult.Ok("should never happen", AiUsage.Empty));
        var sut = CreateSut(provider);

        var result = await sut.CompleteAsync(new AiCompletionRequest { Messages = [AiMessage.User("hi")] });

        Assert.AreEqual(AiCompletionStatus.Refused, result.Status);
        Assert.AreEqual(1, provider.Calls.Count);
    }

    [TestMethod]
    public async Task CompleteAsync_SwappingProfilesIsConfigOnly()
    {
        var provider = new FakeProvider(
            AiCompletionResult.Ok("a", AiUsage.Empty),
            AiCompletionResult.Ok("b", AiUsage.Empty));
        var sut = CreateSut(provider);

        await sut.CompleteAsync(new AiCompletionRequest { Messages = [AiMessage.User("hi")] });
        await sut.CompleteAsync(new AiCompletionRequest { Profile = "classify", Messages = [AiMessage.User("hi")] });

        Assert.AreEqual("claude-opus-5", provider.Calls[0].Profile.Model);
        Assert.AreEqual("claude-haiku-4-5-20251001", provider.Calls[1].Profile.Model);
    }

    [TestMethod]
    public async Task StreamAsync_WithBlankApiKey_YieldsSingleNotConfiguredChunk()
    {
        var sut = CreateSut(new FakeProvider(), apiKey: "");

        var chunks = new List<AiCompletionChunk>();
        await foreach (var chunk in sut.StreamAsync(new AiCompletionRequest { Messages = [AiMessage.User("hi")] }))
            chunks.Add(chunk);

        Assert.AreEqual(1, chunks.Count);
        Assert.IsTrue(chunks[0].IsFinal);
        Assert.AreEqual(AiCompletionStatus.NotConfigured, chunks[0].FinalStatus);
    }

    [TestMethod]
    public void ResolveProfile_UnknownProfileThrows()
    {
        var sut = CreateSut(new FakeProvider());

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            sut.ResolveProfile(new AiCompletionRequest { Profile = "nope" }));
    }

    private static AiCompletionService CreateSut(FakeProvider provider, string apiKey = "key") =>
        new(
            Options.Create(TwoProfileOptions(apiKey)),
            [provider],
            NullLogger<AiCompletionService>.Instance);

    private static AiOptions TwoProfileOptions(string apiKey) => new()
    {
        DefaultProfile = "drafting",
        RetryBaseDelayMilliseconds = 0,
        Providers = { ["anthropic"] = new AiProviderOptions { Type = "anthropic", ApiKey = apiKey } },
        Profiles =
        {
            ["drafting"] = new AiProfileOptions { Provider = "anthropic", Model = "claude-opus-5", Effort = "high" },
            ["classify"] = new AiProfileOptions { Provider = "anthropic", Model = "claude-haiku-4-5-20251001", MaxTokens = 1024 }
        }
    };

    internal sealed class FakeProvider : IAiCompletionProvider
    {
        private readonly Queue<AiCompletionResult> _results;

        public FakeProvider(params AiCompletionResult[] results)
        {
            _results = new Queue<AiCompletionResult>(results);
        }

        public List<(AiProfileOptions Profile, AiCompletionRequest Request)> Calls { get; } = [];

        public string ProviderType => "anthropic";

        public Task<AiCompletionResult> CompleteAsync(ResolvedAiProfile profile, AiCompletionRequest request, CancellationToken ct = default)
        {
            Calls.Add((profile.Profile, request));
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : AiCompletionResult.Ok("ok", AiUsage.Empty));
        }

        public async IAsyncEnumerable<AiCompletionChunk> StreamAsync(ResolvedAiProfile profile, AiCompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Calls.Add((profile.Profile, request));
            yield return AiCompletionChunk.Delta("hello");
            yield return AiCompletionChunk.Final(AiCompletionStatus.Ok, new AiUsage(10, 5, 0, 0));
            await Task.CompletedTask;
        }
    }
}
