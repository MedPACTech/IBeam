namespace IBeam.Api.Models;

public class ApiResponse<T>
{
    public T? Data { get; set; }
    public bool Success { get; set; }
    public List<ApiError> Errors { get; set; } = new();
    public string? TraceId { get; set; }
}
