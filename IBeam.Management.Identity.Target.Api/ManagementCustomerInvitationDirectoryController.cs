using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/tenants/{tenantId:guid}/invites")]
public sealed class ManagementCustomerInvitationDirectoryController(IManagementCustomerInvitationDirectoryService service) : ApiControllerBase
{
    [HttpGet, ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.InvitesRead)]
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] ManagementTenantDirectoryRequest query, CancellationToken ct)
    {
        var page = await service.ListAsync(tenantId, new(query.PageSize, query.ContinuationToken, query.Status, query.NamePrefix), ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken);
    }
    [HttpGet("{inviteId:guid}"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.InvitesRead)]
    public async Task<IActionResult> Find(Guid tenantId, Guid inviteId, CancellationToken ct) => OkResponse(await service.FindAsync(tenantId, inviteId, ct));
}
