namespace IBeam.Credits;

public sealed record CreditGrantInfo(
    Guid CreditGrantId,
    Guid TenantId,
    Guid CreditAccountId,
    string BucketKey,
    decimal Amount,
    string GrantType,
    string RolloverPolicy,
    DateTimeOffset GrantedUtc,
    DateTimeOffset StartsUtc,
    DateTimeOffset? ExpiresUtc,
    string? Period,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static CreditGrantInfo Create(
        Guid tenantId,
        Guid creditAccountId,
        string bucketKey,
        decimal amount,
        string? grantType = null,
        string? rolloverPolicy = null,
        DateTimeOffset? grantedUtc = null,
        DateTimeOffset? startsUtc = null,
        DateTimeOffset? expiresUtc = null,
        string? period = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));
        if (creditAccountId == Guid.Empty)
            throw new ArgumentException("creditAccountId is required.", nameof(creditAccountId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Grant amount must be greater than zero.");

        var starts = startsUtc ?? grantedUtc ?? DateTimeOffset.UtcNow;
        if (expiresUtc is { } expires && expires <= starts)
            throw new ArgumentException("expiresUtc must be after startsUtc.", nameof(expiresUtc));

        return new(
            Guid.NewGuid(),
            tenantId,
            creditAccountId,
            CreditNormalization.NormalizeKey(bucketKey, nameof(bucketKey)),
            amount,
            CreditGrantTypes.Normalize(grantType),
            CreditRolloverPolicies.Normalize(rolloverPolicy),
            grantedUtc ?? DateTimeOffset.UtcNow,
            starts,
            expiresUtc,
            CreditNormalization.NormalizeOptional(period),
            CreditNormalization.NormalizeMetadata(metadata));
    }

    public CreditLedgerEntryInfo ToLedgerEntry()
        => CreditLedgerEntryInfo.CreateGrant(this);
}
