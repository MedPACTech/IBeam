namespace IBeam.Api.Models;

public sealed class ApiOffsetPagedResponse<T> : ApiResponse<IEnumerable<T>>
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public long? TotalCount { get; set; }
}
