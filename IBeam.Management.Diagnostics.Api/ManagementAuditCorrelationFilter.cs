using IBeam.Management;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace IBeam.Management.Diagnostics.Api;
/// <summary>Correlation is a machine-to-machine link, never a human actor assertion.</summary>
internal sealed class ManagementAuditCorrelationFilter(IManagementPrincipalProvider principals, IManagementAuditContext? audit = null) : IResourceFilter, IExceptionFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (audit is null) return;
        var identities = principals.GetPrincipal()?.Identities.Where(x => x.IsAuthenticated).ToArray() ?? [];
        if (identities.Length != 1) return;
        var subject = identities[0].FindAll(ManagementClaimTypes.SubjectType).ToArray();
        var id = identities[0].FindAll(ManagementClaimTypes.CredentialId).ToArray();
        var header = context.HttpContext.Request.Headers["X-IBeam-Management-Correlation"];
        if (subject.Length == 1 && subject[0].Value == ManagementSubjectTypes.Credential && id.Length == 1 &&
            Guid.TryParse(id[0].Value, out var parsed) && parsed != Guid.Empty && header.Count == 1)
            audit.AcceptMachineCorrelation(header[0]!);
    }
    public void OnResourceExecuted(ResourceExecutedContext context) { }
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ManagementAuditUnavailableException) return;
        context.Result = new ObjectResult(new { success = false, errors = new[] { new { code = "Management.AuditUnavailable", message = "Management audit storage is unavailable." } } }) { StatusCode = 503 };
        context.ExceptionHandled = true;
    }
}
