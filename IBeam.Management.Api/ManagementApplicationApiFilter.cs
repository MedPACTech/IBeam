using IBeam.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace IBeam.Management.Api;
internal sealed class ManagementApplicationApiFilter : IResourceFilter, IActionFilter, IExceptionFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
    }
    public void OnResourceExecuted(ResourceExecutedContext context) { }
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid) context.Result = Error(context.HttpContext.TraceIdentifier, 400, "Management.InvalidApplication", "The management application request is invalid.");
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
    public void OnException(ExceptionContext context)
    {
        var status = context.Exception switch
        {
            ManagementAccessDeniedException => 403,
            ManagementApplicationInvalidException => 400,
            ManagementApplicationNotFoundException => 404,
            ManagementApplicationConflictException => 409,
            ManagementApplicationUnavailableException => 503,
            _ => 0
        };
        if (status == 0) return;
        context.Result = Error(context.HttpContext.TraceIdentifier, status, "Management.ApplicationRequestFailed", context.Exception.Message);
        context.ExceptionHandled = true;
    }
    private static ObjectResult Error(string trace, int status, string code, string message)
        => new(new ApiResponse<object> { Success = false, TraceId = trace, Errors = [new ApiError { Code = code, Message = message }] })
        { StatusCode = status };
}
