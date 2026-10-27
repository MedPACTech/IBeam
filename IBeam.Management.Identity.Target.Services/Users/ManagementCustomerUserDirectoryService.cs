using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Identity.Target.Services.Users;

internal sealed class ManagementCustomerUserDirectoryService(IManagementCustomerUserReadRepository users,
    IManagementCustomerTenantScopeService scope, IManagementCustomerIdentityTargetAccessService access,
    ManagementCustomerDirectoryRuntime runtime) : IManagementCustomerUserDirectoryService, IManagementCustomerUserDirectoryQuery
{
    private const string Operation = ManagementCustomerIdentityOperations.UsersRead;
    public Task<ManagementCustomerDirectoryPage<ManagementCustomerUserDto>> ListAsync(Guid tenantId, ManagementCustomerDirectoryQuery query, CancellationToken ct = default)
        => runtime.Run(this, Operation, tenantId, async token =>
        {
            var actor = await scope.RequireAsync(tenantId, Operation, token);
            var state = runtime.Resolve(actor, scope.SourceId, users.SourceId, tenantId, Operation, query, ["active", "disabled"]);
            var page = await users.ReadPageAsync(tenantId, query.PageSize, state.Continuation, token);
            if (page.Items.Count > query.PageSize) throw new ManagementCustomerIdentityUnavailableException();
            var items = page.Items.Where(x => x.TenantId == tenantId).Select(Project).Where(x => x is not null).Cast<ManagementCustomerUserDto>()
                .Where(x => (query.Status is null || x.Status == query.Status) && ManagementCustomerDirectoryRuntime.Prefix(state.Prefix, x.Email, x.FirstName, x.LastName)).ToArray();
            await access.RequireAsync(Operation, token);
            return new ManagementCustomerDirectoryPage<ManagementCustomerUserDto>(items, query.PageSize, runtime.Continue(state, page.ContinuationToken));
        }, ct);
    public Task<ManagementCustomerUserDto?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => runtime.Run<ManagementCustomerUserDto?>(this, Operation, tenantId, async token =>
        {
            await scope.RequireAsync(tenantId, Operation, token);
            if (userId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
            var row = await users.FindAsync(tenantId, userId, token);
            var value = row is null || row.TenantId != tenantId || row.UserId != userId ? null : Project(row);
            if (value is null) throw new ManagementCustomerIdentityNotFoundException();
            await access.RequireAsync(Operation, token); return value;
        }, ct);
    private static ManagementCustomerUserDto? Project(ManagementCustomerUserReadEntity row)
    {
        try
        {
            var status = string.IsNullOrWhiteSpace(row.Status) ? "active" : row.Status.ToLowerInvariant();
            if (row.UserId == Guid.Empty || row.TenantId == Guid.Empty || status is not ("active" or "disabled") || row.Version is not { Length: > 0 and <= 256 }) return null;
            return new(row.TenantId, row.UserId, ManagementCustomerDirectoryRuntime.Email(row.Email), null, null, status,
                ManagementCustomerDirectoryRuntime.Roles(row.RoleIdsCsv), ManagementCustomerDirectoryRuntime.RoleNames(row.RolesCsv, false))
                { CreatedUtc = row.CreatedUtc, UpdatedUtc = row.ModifiedUtc ?? row.DisabledUtc, Version = row.Version };
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException) { return null; }
    }
}
