namespace IBeam.Credits;

public static class CreditLedgerCalculator
{
    public static CreditBalanceInfo CalculateBalance(
        Guid tenantId,
        Guid creditAccountId,
        string bucketKey,
        IEnumerable<CreditLedgerEntryInfo> entries,
        DateTimeOffset? asOfUtc = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId is required.", nameof(tenantId));
        if (creditAccountId == Guid.Empty)
            throw new ArgumentException("creditAccountId is required.", nameof(creditAccountId));

        var bucket = CreditNormalization.NormalizeKey(bucketKey, nameof(bucketKey));
        var asOf = asOfUtc ?? DateTimeOffset.UtcNow;
        var scoped = entries
            .Where(x => x.TenantId == tenantId &&
                        x.CreditAccountId == creditAccountId &&
                        string.Equals(x.BucketKey, bucket, StringComparison.OrdinalIgnoreCase) &&
                        x.EffectiveUtc <= asOf)
            .ToList();
        var granted = scoped.Where(x => x.Amount > 0).Sum(x => x.Amount);
        var expired = scoped.Where(x => x.IsExpired(asOf)).Sum(x => x.Amount);
        var debited = Math.Abs(scoped.Where(x => x.Amount < 0).Sum(x => x.Amount));
        var available = Math.Max(0, granted - expired - debited);

        return new CreditBalanceInfo(
            tenantId,
            creditAccountId,
            bucket,
            granted,
            debited,
            expired,
            available,
            asOf);
    }
}
