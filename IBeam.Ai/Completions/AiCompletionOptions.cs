namespace IBeam.Ai.Completions;

public sealed class AiOptions
{
    public const string SectionName = "IBeam:Ai";

    public string DefaultProfile { get; set; } = string.Empty;
    public Dictionary<string, AiProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, AiProfileOptions> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Retries for transient (Unavailable) failures on CompleteAsync. Refusals are never retried.</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    public bool Validate()
    {
        if (MaxRetryAttempts < 0)
            throw new InvalidOperationException("IBeam:Ai:MaxRetryAttempts must be >= 0.");
        if (RetryBaseDelayMilliseconds < 0)
            throw new InvalidOperationException("IBeam:Ai:RetryBaseDelayMilliseconds must be >= 0.");
        if (!string.IsNullOrWhiteSpace(DefaultProfile) && !Profiles.ContainsKey(DefaultProfile))
            throw new InvalidOperationException($"IBeam:Ai:DefaultProfile '{DefaultProfile}' has no matching entry in IBeam:Ai:Profiles.");

        foreach (var (name, profile) in Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Provider))
                throw new InvalidOperationException($"IBeam:Ai:Profiles:{name}:Provider is required.");
            if (!Providers.ContainsKey(profile.Provider))
                throw new InvalidOperationException($"IBeam:Ai:Profiles:{name} references provider '{profile.Provider}' which is not declared in IBeam:Ai:Providers.");
            if (string.IsNullOrWhiteSpace(profile.Model))
                throw new InvalidOperationException($"IBeam:Ai:Profiles:{name}:Model is required.");
            if (profile.MaxTokens <= 0)
                throw new InvalidOperationException($"IBeam:Ai:Profiles:{name}:MaxTokens must be > 0.");
            if (profile.EstimatedCredits < 0)
                throw new InvalidOperationException($"IBeam:Ai:Profiles:{name}:EstimatedCredits must be >= 0.");
        }

        foreach (var (name, provider) in Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Type))
                throw new InvalidOperationException($"IBeam:Ai:Providers:{name}:Type is required.");
        }

        // A blank ApiKey is deliberately valid: environments without credentials still
        // boot and surface AiCompletionStatus.NotConfigured at call time.
        return true;
    }
}

public sealed class AiProviderOptions
{
    /// <summary>Adapter key, e.g. "anthropic". Matched against IAiCompletionProvider.ProviderType.</summary>
    public string Type { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}

public sealed class AiProfileOptions
{
    /// <summary>Key into AiOptions.Providers.</summary>
    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>Optional effort level: low, medium, high, xhigh, or max. Omit for the model default.</summary>
    public string? Effort { get; set; }

    /// <summary>Optional thinking mode: "adaptive" or "none". Omit to leave the model default.</summary>
    public string? Thinking { get; set; }

    public int MaxTokens { get; set; } = 16000;

    /// <summary>Credit bucket this profile spends from. Null means the profile is unmetered.</summary>
    public string? CreditBucketKey { get; set; }

    /// <summary>Credits reserved before the call, and the settled amount when no token rates are configured.</summary>
    public decimal EstimatedCredits { get; set; }

    /// <summary>When set, actual credits settle as tokens/1M * rate instead of EstimatedCredits.</summary>
    public decimal? CreditsPerMillionInputTokens { get; set; }

    public decimal? CreditsPerMillionOutputTokens { get; set; }

    /// <summary>Credit policy mode for metered calls; defaults to strict-prepaid.</summary>
    public string? CreditPolicyMode { get; set; }

    public bool IsMetered => !string.IsNullOrWhiteSpace(CreditBucketKey);
}

/// <summary>A profile joined with its provider configuration, ready for an adapter.</summary>
public sealed record ResolvedAiProfile(
    string ProfileName,
    AiProviderOptions Provider,
    AiProfileOptions Profile)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Provider.ApiKey);
}
