namespace IBeam.Identity.Seeder;

internal sealed record IdentitySeedRunOptions(bool Apply);

internal sealed class IdentitySeedResult
{
    public bool Applied { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<IdentitySeedChange> Changes { get; } = [];
    public List<IdentitySeedFailure> Failures { get; } = [];

    public IdentitySeedSummary Summary => new(
        Changes.Count(x => x.Action == "create"),
        Changes.Count(x => x.Action == "update"),
        Changes.Count(x => x.Action == "unchanged"),
        Changes.Count(x => x.Action == "skip"),
        Failures.Count);
}

internal sealed record IdentitySeedSummary(
    int Created,
    int Updated,
    int Unchanged,
    int Skipped,
    int Failed);

internal sealed record IdentitySeedChange(
    string Entity,
    string Key,
    string Action,
    string Message);

internal sealed record IdentitySeedFailure(
    string Entity,
    string Key,
    string Message);
