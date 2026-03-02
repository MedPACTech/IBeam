using IBeam.Api.Models;

namespace IBeam.Api.Exceptions;

public sealed class ApiValidationException : Exception
{
    public IReadOnlyCollection<ApiValidationError> Errors { get; }

    public ApiValidationException(IEnumerable<ApiValidationError> errors, string? message = null)
        : base(message ?? "Validation failed.")
    {
        Errors = errors.ToArray();
    }
}
