using IBeam.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult OkResponse<T>(T data)
        => Ok(BuildSuccessResponse(data));

    protected IActionResult DeletedResponse<T>(T data)
        => Ok(BuildSuccessResponse(data));

    protected IActionResult CreatedResponse<T>(string actionName, object? routeValues, T data)
        => CreatedAtAction(actionName, routeValues, BuildSuccessResponse(data));

    protected IActionResult NoContentResponse()
        => NoContent();

    protected IActionResult OkPagedResponse<T>(
        IEnumerable<T> data,
        int pageSize,
        string? continuationToken)
        => OkCursorPagedResponse(data, pageSize, continuationToken);

    protected IActionResult OkCursorPagedResponse<T>(
        IEnumerable<T> data,
        int pageSize,
        string? continuationToken)
        => Ok(new ApiCursorPagedResponse<T>
        {
            Data = data,
            PageSize = pageSize,
            ContinuationToken = continuationToken,
            Success = true,
            TraceId = HttpContext.TraceIdentifier
        });

    protected IActionResult OkOffsetPagedResponse<T>(
        IEnumerable<T> data,
        int pageNumber,
        int pageSize,
        long? totalCount)
        => Ok(new ApiOffsetPagedResponse<T>
        {
            Data = data,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            Success = true,
            TraceId = HttpContext.TraceIdentifier
        });

    protected IActionResult BadRequestResponse(IEnumerable<ApiError> errors)
        => BadRequest(new ApiResponse<object>
        {
            Success = false,
            Errors = errors.ToList(),
            TraceId = HttpContext.TraceIdentifier
        });

    protected IActionResult BadRequestResponse(
        string message,
        string? field = null,
        string code = "Validation")
        => BadRequestResponse(new[]
        {
            new ApiError
            {
                Code = code,
                Field = field,
                Message = message
            }
        });

    private ApiResponse<T> BuildSuccessResponse<T>(T data)
        => new()
        {
            Data = data,
            Success = true,
            TraceId = HttpContext.TraceIdentifier
        };
}
