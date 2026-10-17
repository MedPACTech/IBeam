using IBeam.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace IBeam.Management.Identity.Api;

internal sealed class ManagementSupportApiFilter : IActionFilter, IExceptionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid) context.Result = Error(context.HttpContext.TraceIdentifier, 400);
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
    public void OnException(ExceptionContext context)
    {
        var status = context.Exception switch
        {
            ManagementAccessDeniedException => 403,
            ManagementSupportInvalidException => 400,
            ManagementSupportUnavailableException or ManagementAuditUnavailableException => 503,
            _ => 0
        };
        if (status == 0) return;
        context.Result = Error(context.HttpContext.TraceIdentifier, status); context.ExceptionHandled = true;
    }
    private static ObjectResult Error(string trace, int status) => new(new ApiResponse<object> {
        Success = false, TraceId = trace, Errors = [new ApiError { Code = "Management.SupportRequestFailed",
            Message = "The support request could not be completed." }] }) { StatusCode = status };
}
