namespace IBeam.Credits;

public sealed record CreditLedgerEntryInfo(
    Guid CreditLedgerEntryId,
    Guid TenantId,
    Guid CreditAccountId,
    string BucketKey,
    string EntryType,
    decimal Amount,
    DateTimeOffset EffectiveUtc,
    DateTimeOffset? ExpiresUtc,
    Guid? CreditGrantId,
    string? OperationName,
    string? IdempotencyKey,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool IsExpired(DateTimeOffset asOfUtc)
        => ExpiresUtc is { } expires && expires <= asOfUtc && Amount > 0;

    public static CreditLedgerEntryInfo CreateGrant(CreditGrantInfo grant)
        => new(
            Guid.NewGuid(),
            grant.TenantId,
            grant.CreditAccountId,
            grant.BucketKey,
            CreditLedgerEntryTypes.Grant,
            grant.Amount,
            grant.StartsUtc,
            grant.ExpiresUtc,
            grant.CreditGrantId,
            null,
            null,
            grant.Metadata);

    public static CreditLedgerEntryInfo CreateDebit(
        Guid tenantId,
        Guid creditAccountId,
        string bucketKey,
        decimal amount,
        string? operationName = null,
        string? idempotencyKey = null,
        DateTimeOffset? effectiveUtc = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));
        if (creditAccountId == Guid.Empty)
            throw new ArgumentException("creditAccountId is required.", nameof(creditAccountId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Debit amount must be greater than zero.");

        return new(
            Guid.NewGuid(),
            tenantId,
            creditAccountId,
            CreditNormalization.NormalizeKey(bucketKey, nameof(bucketKey)),
            CreditLedgerEntryTypes.Debit,
            -amount,
            effectiveUtc ?? DateTimeOffset.UtcNow,
            null,
            null,
            CreditNormalization.NormalizeOptional(operationName),
            CreditNormalization.NormalizeOptional(idempotencyKey),
            CreditNormalization.NormalizeMetadata(metadata));
    }
}

public sealed record CreditBalanceInfo(
    Guid TenantId,
    Guid CreditAccountId,
    string BucketKey,
    decimal Granted,
    decimal Debited,
    decimal Expired,
    decimal Available,
    DateTimeOffset AsOfUtc);
