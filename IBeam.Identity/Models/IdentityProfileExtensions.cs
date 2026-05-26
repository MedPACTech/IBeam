namespace IBeam.Identity.Models;

public sealed record IdentityProfileExtensions(
    Guid UserId,
    IReadOnlyDictionary<string, string> Attributes,
    DateTimeOffset? UpdatedAtUtc = null);
