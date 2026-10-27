using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Identity.Target.Services.Users;

internal sealed class ManagementCustomerInvitationDirectoryService(IManagementCustomerInvitationReadRepository invites,
    IManagementCustomerTenantScopeService scope, IManagementCustomerIdentityTargetAccessService access,
    ManagementCustomerDirectoryRuntime runtime, TimeProvider clock) : IManagementCustomerInvitationDirectoryService
{
    private const string Operation = ManagementCustomerIdentityOperations.InvitesRead;
    public Task<ManagementCustomerDirectoryPage<ManagementCustomerInvitationDto>> ListAsync(Guid tenantId, ManagementCustomerDirectoryQuery query, CancellationToken ct = default)
        => runtime.Run(this, Operation, tenantId, async token =>
        {
            var actor = await scope.RequireAsync(tenantId, Operation, token);
            var state = runtime.Resolve(actor, scope.SourceId, invites.SourceId, tenantId, Operation, query, ["pending", "sent", "expired", "redeemed", "revoked"]);
            var page = await invites.ReadPageAsync(tenantId, query.PageSize, state.Continuation, token);
            if (page.Items.Count > query.PageSize) throw new ManagementCustomerIdentityUnavailableException();
            var items = page.Items.Where(x => x.TenantId == tenantId).Select(Project).Where(x => x is not null).Cast<ManagementCustomerInvitationDto>()
                .Where(x => (query.Status is null || x.Status == query.Status) && ManagementCustomerDirectoryRuntime.Prefix(state.Prefix, x.Email, x.FirstName, x.LastName)).ToArray();
            await access.RequireAsync(Operation, token);
            return new ManagementCustomerDirectoryPage<ManagementCustomerInvitationDto>(items, query.PageSize, runtime.Continue(state, page.ContinuationToken));
        }, ct);
    public Task<ManagementCustomerInvitationDto?> FindAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default)
        => runtime.Run<ManagementCustomerInvitationDto?>(this, Operation, tenantId, async token =>
        {
            await scope.RequireAsync(tenantId, Operation, token);
            if (inviteId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
            var row = await invites.FindAsync(tenantId, inviteId, token);
            var value = row is null || row.TenantId != tenantId || row.InviteId != inviteId ? null : Project(row);
            if (value is null) throw new ManagementCustomerIdentityNotFoundException();
            await access.RequireAsync(Operation, token); return value;
        }, ct);
    private ManagementCustomerInvitationDto? Project(ManagementCustomerInvitationReadEntity row)
    {
        try
        {
            var status = row.Status;
            if (row.InviteId == Guid.Empty || row.TenantId == Guid.Empty || !row.ExpiresUtc.HasValue ||
                status is not ("pending" or "sent" or "expired" or "redeemed" or "revoked") || row.Version is not { Length: > 0 and <= 256 }) return null;
            if (status is "pending" or "sent" && row.ExpiresUtc.Value <= clock.GetUtcNow()) status = "expired";
            return new(row.TenantId, row.InviteId, row.DestinationType == "email" ? ManagementCustomerDirectoryRuntime.Email(row.Destination) : null,
                ManagementCustomerDirectoryRuntime.Name(row.FirstName), ManagementCustomerDirectoryRuntime.Name(row.LastName), status,
                ManagementCustomerDirectoryRuntime.Roles(row.RoleIdsCsv), ManagementCustomerDirectoryRuntime.RoleNames(row.RoleNamesJson, true),
                row.CreatedUtc, row.ExpiresUtc, row.ModifiedUtc ?? row.RevokedUtc ?? row.RedeemedUtc ?? row.SentUtc, row.Version);
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException) { return null; }
    }
}
