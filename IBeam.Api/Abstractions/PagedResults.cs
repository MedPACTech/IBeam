namespace IBeam.Api.Abstractions;

public sealed record CursorPagedResult<TEntity>(
    IEnumerable<TEntity> Results,
    string? ContinuationToken);

public sealed record OffsetPagedResult<TEntity>(
    IEnumerable<TEntity> Results,
    int PageNumber,
    int PageSize,
    long? TotalCount);
