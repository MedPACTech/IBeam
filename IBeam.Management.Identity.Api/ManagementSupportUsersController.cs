using IBeam.Api.Controllers;
using IBeam.Management.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Api;

[NonController]
[Authorize(Policy = ManagementApiPolicies.Access)]
[ServiceFilter(typeof(ManagementSupportApiFilter), Order = -3000)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/management")]
public sealed class ManagementSupportUsersController(IManagementSupportUserService users) : ApiControllerBase
{
    [HttpGet("support-users")]
    public async Task<IActionResult> ListAsync(CancellationToken ct) => OkResponse(await users.ListAsync(ct));
    [HttpPost("support-invitations")]
    public async Task<IActionResult> InviteAsync(ManagementSupportInvitationRequest request, CancellationToken ct)
        => OkResponse(await users.InviteAsync(request, ct));
    [AllowAnonymous, HttpPost("support-invitations/accept")]
    [RequestSizeLimit(16384), HttpLogging(HttpLoggingFields.None)]
    public async Task<IActionResult> AcceptAsync(ManagementSupportInvitationAcceptRequest request, CancellationToken ct)
        => OkResponse(await users.AcceptAsync(request, ct));
    [HttpPatch("support-users/{userId:guid}")]
    public async Task<IActionResult> DisableAsync(Guid userId, DisableManagementSupportUser request, CancellationToken ct)
    {
        if (request.IsEnabled) throw new ManagementSupportInvalidException();
        await users.DisableAsync(userId, ct);
        return OkResponse(new { userId, isEnabled = false });
    }
}
public sealed class DisableManagementSupportUser { public bool IsEnabled { get; set; } }
