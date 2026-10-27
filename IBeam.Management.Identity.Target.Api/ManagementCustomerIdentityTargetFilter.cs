using IBeam.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Identity.Target.Api;

internal sealed class ManagementCustomerIdentityTargetFilter : IResourceFilter, IActionFilter, IExceptionFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context) => context.HttpContext.Response.Headers.CacheControl = "no-store";
    public void OnResourceExecuted(ResourceExecutedContext context) { }
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid) context.Result = Error(context.HttpContext.TraceIdentifier, 400);
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
    public void OnException(ExceptionContext context)
    {
        var status = context.Exception switch { ManagementAccessDeniedException => 403,
            ManagementCustomerIdentityInvalidRequestException => 400, ManagementCustomerIdentityNotFoundException => 404,
            ManagementIdentityOperationConflictException => 409,
            ManagementCustomerIdentityPreconditionException => 412,
            ManagementCustomerIdentityRateLimitException => 429,
            ManagementCustomerIdentityUnavailableException => 503,
            ManagementConnectionInvalidException => 503, ManagementAuditUnavailableException => 503, _ => 0 };
        if (status == 0) return;
        context.Result = Error(context.HttpContext.TraceIdentifier, status);
        context.ExceptionHandled = true;
    }
    private static ObjectResult Error(string trace, int status) => new(new ApiResponse<object> { Success = false,
        TraceId = trace, Errors = [new ApiError { Code = "Management.CustomerIdentityRequestFailed",
            Message = status switch { 400 => "The customer management request is invalid.", 403 => "Management access is not permitted.",
                404 => "The customer management resource was not found.", 409 => "The management operation conflicts with an existing request.",
                412 => "The resource version has changed; read its current version before retrying.",
                429 => "Customer management is temporarily rate limited.",
                _ => "Customer management is temporarily unavailable." } }] }) { StatusCode = status };
}
