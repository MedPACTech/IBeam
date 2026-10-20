using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;

[NonController]
[Authorize(Policy = ManagementApiPolicies.Access)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[HttpLogging(HttpLoggingFields.None)]
[Route("api/management/applications/{applicationId:guid}/tenants")]
public sealed class ManagementPortalCustomerDirectoriesController(IManagementPortalCustomerDirectory<ManagementCustomerTenantDto> tenants,
    IManagementPortalCustomerDirectory<ManagementCustomerUserDto> users, IManagementPortalCustomerDirectory<ManagementCustomerInvitationDto> invites) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> TenantsAsync(Guid applicationId, [FromQuery] ManagementCustomerDirectoryQuery query, CancellationToken ct) {
        var page = await tenants.ListAsync(applicationId, null, query, ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken); }
    [HttpGet("{tenantId:guid}")]
    public async Task<IActionResult> TenantAsync(Guid applicationId, Guid tenantId, CancellationToken ct)
        => OkResponse(await tenants.FindAsync(applicationId, null, tenantId, ct));
    [HttpGet("{tenantId:guid}/users")]
    public async Task<IActionResult> UsersAsync(Guid applicationId, Guid tenantId, [FromQuery] ManagementCustomerDirectoryQuery query, CancellationToken ct) {
        var page = await users.ListAsync(applicationId, tenantId, query, ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken); }
    [HttpGet("{tenantId:guid}/users/{userId:guid}")]
    public async Task<IActionResult> UserAsync(Guid applicationId, Guid tenantId, Guid userId, CancellationToken ct)
        => OkResponse(await users.FindAsync(applicationId, tenantId, userId, ct));
    [HttpGet("{tenantId:guid}/invites")]
    public async Task<IActionResult> InvitesAsync(Guid applicationId, Guid tenantId, [FromQuery] ManagementCustomerDirectoryQuery query, CancellationToken ct) {
        var page = await invites.ListAsync(applicationId, tenantId, query, ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken); }
    [HttpGet("{tenantId:guid}/invites/{inviteId:guid}")]
    public async Task<IActionResult> InviteAsync(Guid applicationId, Guid tenantId, Guid inviteId, CancellationToken ct)
        => OkResponse(await invites.FindAsync(applicationId, tenantId, inviteId, ct));
}
