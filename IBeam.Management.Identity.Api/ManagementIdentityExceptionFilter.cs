using IBeam.Api.Models;
using IBeam.Identity.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IBeam.Management.Identity.Api;

internal sealed class ManagementIdentityExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not (ManagementAccessDeniedException or IdentityUnauthorizedException or IdentityValidationException))
            return;
        // Never expose account existence, OTP details, credentials, or underlying provider exception text.
        context.Result = new ObjectResult(new ApiResponse<object>
        {
            Success = false, TraceId = context.HttpContext.TraceIdentifier,
            Errors = [new ApiError { Code = "Management.AuthenticationDenied", Message = "Management authentication was not completed." }]
        }) { StatusCode = 401 };
        context.ExceptionHandled = true;
    }
}
