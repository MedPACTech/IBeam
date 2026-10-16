using IBeam.Api.Models;
using IBeam.Management.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IBeam.Management.Diagnostics.Api.Controllers;

/// <summary>Safe IBeam envelopes. Runs before automatic model-state responses can echo attempted secret values.</summary>
internal sealed class SystemErrorApiFilter : IResourceFilter, IActionFilter, IExceptionFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
    }
    public void OnResourceExecuted(ResourceExecutedContext context) { }
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
            context.Result = Error(context.HttpContext, 400, "Management.InvalidQuery", "The diagnostic query is invalid.");
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
    public void OnException(ExceptionContext context)
    {
        var failure = context.Exception switch
        {
            ManagementAccessDeniedException => (403, "Management.AccessDenied", "Management access is not permitted."),
            DiagnosticQueryException or DiagnosticTokenException => (400, "Management.InvalidQuery", "The diagnostic query is invalid."),
            ManagementDiagnosticNotFoundException => (404, "Management.DiagnosticNotFound", "Diagnostic record was not found."),
            ManagementDiagnosticUnavailableException => (503, "Management.DiagnosticsUnavailable", "Diagnostic reads are temporarily unavailable."),
            OperationCanceledException when !context.HttpContext.RequestAborted.IsCancellationRequested
                => (504, "Management.DiagnosticsTimeout", "The diagnostic read timed out."),
            _ => (0, "", "")
        };
        if (failure.Item1 == 0) return;
        context.Result = Error(context.HttpContext, failure.Item1, failure.Item2, failure.Item3);
        context.ExceptionHandled = true;
    }
    private static ObjectResult Error(HttpContext context, int status, string code, string message)
        => new(new ApiResponse<object>
        {
            Success = false, TraceId = context.TraceIdentifier,
            Errors = [new ApiError { Code = code, Message = message }]
        }) { StatusCode = status };
}
