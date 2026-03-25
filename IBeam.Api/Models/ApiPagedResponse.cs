namespace IBeam.Api.Models;

public sealed class ApiPagedResponse<T> : ApiResponse<IEnumerable<T>>
{
    public int? PageSize { get; set; }
    public string? ContinuationToken { get; set; }
}
