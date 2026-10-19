using IBeam.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace IBeam.Management.Identity.Target.Api;

internal sealed class ManagementCustomerIdentityTargetFilter : IResourceFilter, IExceptionFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context) => context.HttpContext.Response.Headers.CacheControl = "no-store";
    public void OnResourceExecuted(ResourceExecutedContext context) { }
    public void OnException(ExceptionContext context)
    {
        var status = context.Exception switch { ManagementAccessDeniedException => 403,
            ManagementConnectionInvalidException => 503, ManagementAuditUnavailableException => 503, _ => 0 };
        if (status == 0) return;
        context.Result = new ObjectResult(new ApiResponse<object> { Success = false,
            TraceId = context.HttpContext.TraceIdentifier, Errors = [new ApiError { Code = "Management.CustomerIdentityRequestFailed",
                Message = status == 403 ? "Management access is not permitted." : "Customer management is temporarily unavailable." }] }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
