namespace IBeam.Api.Models;

public sealed class ApiError
{
    public string Code { get; set; } = "Error";
    public string? Field { get; set; }
    public string Message { get; set; } = string.Empty;
}
