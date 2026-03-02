namespace IBeam.Api.Models;

public sealed class ApiValidationError
{
    public string Code { get; set; } = "Validation";
    public string? Field { get; set; }
    public string Message { get; set; } = string.Empty;
}
