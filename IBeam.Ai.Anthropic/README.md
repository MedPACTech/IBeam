# IBeam.Ai.Anthropic

Anthropic (Claude) provider adapter for the IBeam.Ai completions layer.

Register it next to `AddIBeamAiCompletions` and declare an `anthropic` provider in configuration:

```csharp
builder.Services
    .AddIBeamAiCompletions(builder.Configuration)
    .AddIBeamAiAnthropic();
```

```json
"IBeam": {
  "Ai": {
    "DefaultProfile": "drafting",
    "Providers": {
      "anthropic": { "Type": "anthropic", "ApiKey": "" }
    },
    "Profiles": {
      "drafting": { "Provider": "anthropic", "Model": "claude-opus-5", "Effort": "high", "MaxTokens": 16000 }
    }
  }
}
```

A blank `ApiKey` is valid: the environment boots and calls return `AiCompletionStatus.NotConfigured`.
Switching model, effort, or budget is a configuration edit — no code change in the consuming app.
