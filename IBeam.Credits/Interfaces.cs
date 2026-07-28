namespace IBeam.Credits;

public interface ICreditLedgerStore
{
    Task AppendLedgerEntryAsync(CreditLedgerEntryInfo entry, CancellationToken ct = default);

    Task<IReadOnlyList<CreditLedgerEntryInfo>> ListLedgerEntriesAsync(
        Guid tenantId,
        Guid creditAccountId,
        string? bucketKey = null,
        CancellationToken ct = default);
}
