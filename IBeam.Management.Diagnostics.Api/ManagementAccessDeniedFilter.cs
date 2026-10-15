using IBeam.Api.Models;
using IBeam.Management;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IBeam.Management.Diagnostics.Api;

// Expected service denials are not unexpected system failures and never include principal details.
internal sealed class ManagementAccessDeniedFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ManagementAccessDeniedException) return;
        context.Result = new ObjectResult(new ApiResponse<object>
        {
            Success = false,
            TraceId = context.HttpContext.TraceIdentifier,
            Errors = [new ApiError { Code = "Management.AccessDenied", Message = "Management access is not permitted." }]
        }) { StatusCode = 403 };
        context.ExceptionHandled = true;
    }
}
