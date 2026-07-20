namespace IBeam.Api.Models;

public sealed class ApiCursorPagedResponse<T> : ApiResponse<IEnumerable<T>>
{
    public int PageSize { get; set; }
    public string? ContinuationToken { get; set; }
}
