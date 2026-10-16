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
        if (context.Exception is not (ManagementAccessDeniedException or ManagementAuditUnavailableException)) return;
        context.Result = new ObjectResult(new ApiResponse<object>
        {
            Success = false,
            TraceId = context.HttpContext.TraceIdentifier,
            Errors = [new ApiError { Code = "Management.AccessDenied", Message = context.Exception is ManagementAuditUnavailableException ? "Management audit storage is unavailable." : "Management access is not permitted." }]
        }) { StatusCode = context.Exception is ManagementAuditUnavailableException ? 503 : 403 };
        context.ExceptionHandled = true;
    }
}
