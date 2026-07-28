namespace IBeam.Credits;

public sealed record CreditAccountInfo(
    Guid CreditAccountId,
    Guid TenantId,
    string DisplayName,
    string Status,
    string? SubjectType,
    string? SubjectId,
    DateTimeOffset CreatedUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static CreditAccountInfo Create(
        Guid tenantId,
        string displayName,
        string? subjectType = null,
        string? subjectId = null,
        string? status = null,
        DateTimeOffset? createdUtc = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));

        return new(
            Guid.NewGuid(),
            tenantId,
            CreditNormalization.NormalizeRequired(displayName, nameof(displayName)),
            CreditAccountStatuses.Normalize(status),
            CreditNormalization.NormalizeOptional(subjectType),
            CreditNormalization.NormalizeOptional(subjectId),
            createdUtc ?? DateTimeOffset.UtcNow,
            CreditNormalization.NormalizeMetadata(metadata));
    }
}
