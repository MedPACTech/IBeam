using IBeam.Api.Abstractions;
using IBeam.Api.Exceptions;
using IBeam.Api.Models;
using IBeam.Utilities.Exceptions;
using Microsoft.Extensions.Options;

namespace IBeam.Api.Middleware;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;
    private readonly ApiErrorHandlingOptions _options;
    private readonly IApiErrorSink? _errorSink;

    public ApiExceptionMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionMiddleware> logger,
        IOptions<ApiErrorHandlingOptions> options,
        IApiErrorSink? errorSink = null)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
        _errorSink = errorSink;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiValidationException vex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new ApiResponse<object>
            {
                Success = false,
                Errors = vex.Errors.Select(e => new ApiError
                {
                    Code = e.Code,
                    Field = e.Field,
                    Message = e.Message
                }).ToList(),
                TraceId = _options.IncludeTraceIdInResponse ? context.TraceIdentifier : null
            });
        }
        catch (Exception ex)
        {
            await PersistErrorAsync(context, ex, "RequestPipeline", context.RequestAborted);

            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var baseException = ex as IBaseException;
            var defaultMessage = _options.ExposeDetailedErrors
                ? ex.Message
                : "An unexpected error occurred.";

            string message;
            string code;

            if (baseException is not null)
            {
                context.Response.StatusCode = (int)baseException.StatusCode;
                message = baseException.UserMessage;
                code = baseException.Code;
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                message = defaultMessage;
                code = "UnexpectedError";
            }

            await context.Response.WriteAsJsonAsync(new ApiResponse<object>
            {
                Success = false,
                Errors = new List<ApiError>
                {
                    new()
                    {
                        Code = code,
                        Message = message
                    }
                },
                TraceId = _options.IncludeTraceIdInResponse ? context.TraceIdentifier : null
            });
        }
    }

    private async Task PersistErrorAsync(
        HttpContext context,
        Exception ex,
        string source,
        CancellationToken cancellationToken)
    {
        if (_errorSink is null)
        {
            return;
        }

        try
        {
            await _errorSink.SaveAsync(new ApiErrorRecord
            {
                Source = source,
                Path = context.Request.Path,
                Method = context.Request.Method,
                Message = ex.Message,
                Exception = ex.ToString(),
                TraceId = context.TraceIdentifier,
                Timestamp = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        catch (Exception saveEx)
        {
            _logger.LogWarning(saveEx, "Failed to persist API exception details.");
        }
    }
}
