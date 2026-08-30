using IBeam.Ai.Anthropic;
using IBeam.Ai.Completions;

namespace IBeam.Tests.Ai;

[TestClass]
public sealed class AnthropicCompletionProviderTests
{
    [TestMethod]
    public void BuildOutputConfig_MapsEffortLevels()
    {
        foreach (var effort in new[] { "low", "medium", "high", "xhigh", "max", "HIGH" })
        {
            var config = AnthropicCompletionProvider.BuildOutputConfig(effort, null);
            Assert.IsNotNull(config, $"Effort '{effort}' should produce an output config.");
        }
    }

    [TestMethod]
    public void BuildOutputConfig_UnknownEffortThrows()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            AnthropicCompletionProvider.BuildOutputConfig("extreme", null));
    }

    [TestMethod]
    public void BuildOutputConfig_WithNoEffortOrSchema_ReturnsNull()
    {
        Assert.IsNull(AnthropicCompletionProvider.BuildOutputConfig(null, null));
    }

    [TestMethod]
    public void BuildOutputConfig_ParsesJsonSchemaForStructuredOutput()
    {
        var config = AnthropicCompletionProvider.BuildOutputConfig(null, new AiJsonSchema
        {
            Name = "draft",
            SchemaJson = """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}"""
        });

        Assert.IsNotNull(config);
        Assert.IsNotNull(config.Format);
    }

    [TestMethod]
    public void BuildParameters_MapsProfileAndMessages()
    {
        var profile = new ResolvedAiProfile(
            "drafting",
            new AiProviderOptions { Type = "anthropic", ApiKey = "key" },
            new AiProfileOptions { Provider = "anthropic", Model = "claude-opus-5", MaxTokens = 2048, Thinking = "adaptive" });

        var parameters = AnthropicCompletionProvider.BuildParameters(profile, new AiCompletionRequest
        {
            System = "You are helpful.",
            Messages = [AiMessage.User("hello"), AiMessage.Assistant("hi"), AiMessage.User("draft this")]
        });

        StringAssert.Contains(parameters.Model.ToString(), "claude-opus-5");
        Assert.AreEqual(2048, parameters.MaxTokens);
        Assert.AreEqual(3, parameters.Messages.Count);
        Assert.IsNotNull(parameters.System);
        Assert.IsNotNull(parameters.Thinking);
    }
}
