namespace IBeam.Api.Models;

public sealed class ApiErrorHandlingOptions
{
    public bool ExposeDetailedErrors { get; set; }
    public bool IncludeTraceIdInResponse { get; set; } = true;
}
