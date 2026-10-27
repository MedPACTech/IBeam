using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Identity.Services.Tenants;

/// <summary>Native ownership business rules. No support/customer impersonation or global account mutation.</summary>
public sealed class IdentityTenantOwnershipAdministrationService : IIdentityTenantOwnershipAdministrationService
{
    private readonly IIdentityTenantOwnershipTransferStore store;
    private readonly IIdentityTenantOwnershipRoleStore roles;
    private readonly IIdentityTenantStore tenants;
    private readonly IServiceOperationExecutor operations;
    private readonly IdentityTenantOwnershipTransferOptions policy;
    private readonly string[] ownerNames;
    public IdentityTenantOwnershipAdministrationService(IIdentityTenantMembershipConditionalStore memberships, ITenantRoleStore roleStore,
        IIdentityTenantStore tenants, IOptions<IdentityTenantOwnershipOptions> ownership, IOptions<IdentityTenantOwnershipTransferOptions> options,
        IServiceOperationExecutor operations)
    {
        var configured = options.Value; var owners = ownership.Value;
        if (!configured.Enabled || !configured.IsValid() || !owners.Enabled || !owners.IsValid() ||
            memberships is not IIdentityTenantOwnershipTransferStore { OwnershipProtocolVersion: 1, RequiresOwnershipProtection: true } transfer ||
            roleStore is not IIdentityTenantOwnershipRoleStore { OwnershipProtocolVersion: 1, RequiresOwnershipProtection: true } definitions)
            throw new InvalidOperationException("Explicit ownership transfer policy and compatible atomic native ownership writers are required.");
        store = transfer; roles = definitions; this.tenants = tenants; this.operations = operations;
        policy = new() { Enabled = true, Mode = configured.Mode, MaximumMembers = configured.MaximumMembers };
        ownerNames = owners.OwnerRoleNames.ToArray();
    }
    public Task<IdentityTenantOwnerSnapshot> ReadOwnersAsync(Guid tenant, CancellationToken ct = default)
        => Run(tenant, null, "read", async token => {
            var (snapshot, definitions) = await Snapshot(tenant, token);
            return new IdentityTenantOwnerSnapshot(tenant, snapshot.Version, snapshot.Members.Where(x => Owner(x, definitions))
                .OrderBy(x => x.UserId).Select(x => new IdentityTenantOwner(x.UserId, x.Version)).ToArray());
        }, ct);
    public Task<IdentityTenantOwnershipTransferResult> TransferAsync(IdentityTenantOwnershipTransferRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new IdentityValidationException("Ownership request is required.");
        return Run(request.TenantId, request.SourceUserId, "transfer", async token => {
            Validate(request);
            var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] {
                request.TenantId.ToString("D"), request.SourceUserId.ToString("D"), request.DestinationUserId.ToString("D"),
                request.SourceVersion, request.DestinationVersion, request.OwnershipVersion, policy.Mode.ToString() })));
            var receipt = await store.FindOwnershipTransferAsync(request.TenantId, request.OperationId, token);
            if (receipt is not null) return await Complete(receipt, request, hash, token);
            var tenant = await tenants.FindByIdAsync(request.TenantId, token);
            if (tenant is null || tenant.TenantId != request.TenantId || !tenant.IsActive) throw new IdentityValidationException("Tenant is unavailable.");
            var (snapshot, definitions) = await Snapshot(request.TenantId, token);
            var source = snapshot.Members.SingleOrDefault(x => x.UserId == request.SourceUserId) ?? throw new IdentityNotFoundException("Source membership was not found.");
            var destination = snapshot.Members.SingleOrDefault(x => x.UserId == request.DestinationUserId) ?? throw new IdentityNotFoundException("Destination membership was not found.");
            if (snapshot.Version != request.OwnershipVersion || source.Version != request.SourceVersion || destination.Version != request.DestinationVersion)
                throw new IdentityConcurrencyException();
            if (!Owner(source, definitions) || !destination.IsEnabled || Owner(destination, definitions))
                throw new IdentityValidationException("Transfer requires an enabled owner and an enabled non-owner destination in the same tenant.");
            if (policy.Mode == IdentityTenantOwnerMode.SingleOwner && snapshot.Members.Count(x => Owner(x, definitions)) != 1)
                throw new IdentityValidationException("Single-owner transfer requires exactly one current owner; reconcile existing owners first.");
            if (source.RoleIds.Count == 0 && source.RoleNames.Count != 0 || destination.RoleIds.Count == 0 && destination.RoleNames.Count != 0)
                throw new IdentityValidationException("Normalize legacy role-name-only memberships before ownership transfer.");
            var transferred = definitions.Where(x => source.RoleIds.Contains(x.RoleId)).ToArray();
            var nextSource = source with {
                RoleIds = source.RoleIds.Except(transferred.Select(x => x.RoleId)).Order().ToArray(),
                RoleNames = source.RoleNames.Except(transferred.Select(x => x.Name), StringComparer.OrdinalIgnoreCase).ToArray() };
            var nextDestination = destination with {
                RoleIds = destination.RoleIds.Concat(transferred.Select(x => x.RoleId)).Distinct().Order().ToArray(),
                RoleNames = destination.RoleNames.Concat(transferred.Select(x => x.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
            if (transferred.Length == 0 || nextDestination.RoleIds.Count > 32 || nextDestination.RoleNames.Count > 32)
                throw new IdentityValidationException("Ownership transfer exceeds the supported role bound.");
            var after = snapshot.Members.Where(x => x.UserId != source.UserId && x.UserId != destination.UserId).Append(nextSource).Append(nextDestination).ToArray();
            if (!after.Any(x => Owner(x, definitions))) throw new IdentityValidationException("An eligible owner must remain.");
            if (snapshot.ProtectedAdministratorRoleIds.Count != 0) {
                var administrators = await Resolve(snapshot.TenantId, snapshot.ProtectedAdministratorRoleIds, token);
                if (!after.Any(x => x.IsEnabled && (x.RoleIds.Intersect(snapshot.ProtectedAdministratorRoleIds).Any() ||
                    x.RoleIds.Count == 0 && x.RoleNames.Intersect(administrators.Select(r => r.Name), StringComparer.OrdinalIgnoreCase).Any())))
                    throw new IdentityValidationException("An eligible administrator must remain.");
            }
            receipt = await store.CommitOwnershipTransferAsync(new(nextSource, nextDestination, snapshot.Version, request.OperationId, hash), token);
            return await Complete(receipt, request, hash, token);
        }, ct);
    }
    private async Task<IdentityTenantOwnershipTransferResult> Complete(IdentityTenantOwnershipTransferReceipt receipt,
        IdentityTenantOwnershipTransferRequest request, string hash, CancellationToken ct)
    {
        if (receipt.TenantId != request.TenantId || receipt.OperationId != request.OperationId || receipt.SourceUserId != request.SourceUserId ||
            receipt.DestinationUserId != request.DestinationUserId || receipt.IntentHash != hash) throw new IdentityConcurrencyException();
        // Rebuild from current authoritative rows, never replay old roles over subsequent changes.
        await store.RepairProjectionAsync(request.TenantId, request.SourceUserId, ct);
        await store.RepairProjectionAsync(request.TenantId, request.DestinationUserId, ct);
        return new(receipt.TenantId, receipt.OperationId, receipt.SourceUserId, receipt.DestinationUserId);
    }
    private async Task<(IdentityTenantMembershipSnapshot Snapshot, IReadOnlyList<TenantRole> Definitions)> Snapshot(Guid tenant, CancellationToken ct)
    {
        var snapshot = await store.ReadOwnershipSnapshotAsync(tenant, policy.MaximumMembers, ct);
        if (snapshot.TenantId != tenant || !Exact(snapshot.Version) || snapshot.OwnershipProtocolVersion != 1 ||
            snapshot.ProtectedOwnerRoleIds is not { Count: > 0 and <= 8 } || snapshot.ProtectedOwnerRoleIds.Any(x => x == Guid.Empty) ||
            snapshot.ProtectedOwnerRoleIds.Distinct().Count() != snapshot.ProtectedOwnerRoleIds.Count ||
            snapshot.ProtectedAdministratorRoleIds.Count > 8 || snapshot.ProtectedAdministratorRoleIds.Any(x => x == Guid.Empty) ||
            snapshot.Members.Count > policy.MaximumMembers || snapshot.Members.Select(x => x.UserId).Distinct().Count() != snapshot.Members.Count ||
            snapshot.Members.Any(x => x.TenantId != tenant || x.UserId == Guid.Empty || !Exact(x.Version) || x.RoleIds.Count > 32 || x.RoleNames.Count > 32 ||
                x.RoleIds.Any(id => id == Guid.Empty) || x.RoleIds.Distinct().Count() != x.RoleIds.Count ||
                x.RoleNames.Any(n => string.IsNullOrWhiteSpace(n) || n.Length > 100 || n.Contains(',') || n.Any(char.IsControl))))
            throw new IdentityProviderException("Ownership snapshot is invalid.");
        var definitions = await roles.ReadOwnershipRolesAsync(tenant, ownerNames, ct);
        if (definitions.Count != ownerNames.Length || definitions.Any(x => x.TenantId != tenant || x.RoleId == Guid.Empty || !x.IsActive || !x.IsSystem ||
                !ownerNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase)) || definitions.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Count ||
            !snapshot.ProtectedOwnerRoleIds.Order().SequenceEqual(definitions.Select(x => x.RoleId).Order()))
            throw new IdentityValidationException("Configured immutable owner roles do not match the protected snapshot.");
        return (snapshot, definitions);
    }
    private async Task<IReadOnlyList<TenantRole>> Resolve(Guid tenant, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var result = new List<TenantRole>();
        foreach (var id in ids) {
            var role = await roles.GetRoleAsync(tenant, id, ct);
            if (role is null || role.TenantId != tenant || role.RoleId != id || !role.IsActive || !role.IsSystem)
                throw new IdentityValidationException("Protected administrator roles are unavailable.");
            result.Add(role);
        }
        return result;
    }
    private static bool Owner(IdentityTenantMembershipState member, IReadOnlyList<TenantRole> roles)
        => member.IsEnabled && (member.RoleIds.Intersect(roles.Select(x => x.RoleId)).Any() ||
            member.RoleIds.Count == 0 && member.RoleNames.Intersect(roles.Select(x => x.Name), StringComparer.OrdinalIgnoreCase).Any());
    private static bool Exact(string? version) => !string.IsNullOrWhiteSpace(version) && version != "*" && version.Length <= 256;
    private static void Validate(IdentityTenantOwnershipTransferRequest request)
    {
        if (request.OperationId == Guid.Empty || request.SourceUserId == Guid.Empty || request.DestinationUserId == Guid.Empty ||
            request.SourceUserId == request.DestinationUserId) throw new IdentityValidationException("Ownership transfer scope is invalid.");
        if (!Exact(request.SourceVersion) || !Exact(request.DestinationVersion) || !Exact(request.OwnershipVersion)) throw new IdentityConcurrencyException();
    }
    private Task<T> Run<T>(Guid tenant, Guid? user, string action, Func<CancellationToken, Task<T>> body, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            if (tenant == Guid.Empty) throw new IdentityValidationException("Ownership scope is invalid.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try { return await body(timeout.Token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (IdentityConcurrencyException) { throw; }
            catch (IdentityValidationException) { throw new IdentityValidationException("Ownership request or policy is invalid."); }
            catch (IdentityNotFoundException) { throw new IdentityNotFoundException("Ownership membership was not found."); }
            catch { throw new IdentityProviderException("Ownership administration is unavailable; retry the same operation to reconcile an uncertain result."); }
        }, new ServiceOperationExecutionOptions { OperationName = "identity.tenantownership." + action, TenantId = tenant, EntityId = user,
            PermissionEnabled = false, AuditEnabled = false }, ct); // Trusted adapter; the calling service authorizes and audits the actor.
}
