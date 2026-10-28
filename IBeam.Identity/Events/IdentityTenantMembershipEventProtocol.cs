using IBeam.Identity.Exceptions;

namespace IBeam.Identity.Events;

/// <summary>Pure schema/metadata protocol shared by atomic native persistence and service projection. A candidate is not published until its native transaction commits.</summary>
public static class IdentityTenantMembershipEventProtocol
{
    public static TenantMembershipChangedEvent? ProjectCommitted(IdentityMembershipCommittedChange value)
    {
        if (value is null || !Enum.IsDefined(value.Disposition)) Fail();
        if (value!.Disposition != IdentityMembershipCommitDisposition.Committed) return null;
        var scope = value.Scope; var actor = value.Actor;
        if (value.EventId == Guid.Empty || value.OperationId == Guid.Empty || value.Sequence < 1 || value.OccurredUtc.Offset != TimeSpan.Zero || value.OccurredUtc < DateTimeOffset.UnixEpoch ||
            scope is null || !Key(scope.ApplicationKey) || !Key(scope.Environment) || scope.RealmId == Guid.Empty || scope.TenantId == Guid.Empty ||
            actor is null || !Enum.IsDefined(actor.Kind) || actor.CorrelationId == Guid.Empty ||
            actor.Kind == IdentityMembershipActorKind.Support && (Empty(actor.UserId) || Empty(actor.ManagementOrganizationId) || Empty(actor.ManagementApplicationId)) ||
            actor.Kind == IdentityMembershipActorKind.Customer && (Empty(actor.UserId) || actor.ManagementOrganizationId is not null || actor.ManagementApplicationId is not null) ||
            actor.Kind is IdentityMembershipActorKind.Unknown or IdentityMembershipActorKind.System &&
                (actor.UserId is not null || actor.ManagementOrganizationId is not null || actor.ManagementApplicationId is not null) ||
            value.Changes is not { Count: > 0 and <= 2 } || value.ProtectedOwnerRoleIds is null) Fail();
        var changes = value.Changes!.Select(Change).ToArray();
        if (changes.Select(x => x.UserId).Distinct().Count() != changes.Length) Fail();
        var owners = Roles(value.ProtectedOwnerRoleIds!, 8);
        string kind;
        if (owners.Length != 0) {
            if (changes.Length != 2 || changes.Any(x => x.Before is null || x.After is null || !x.Before.IsEnabled || !x.After.IsEnabled)) Fail();
            bool Owner(IdentityMembershipEventState state) => state.RoleIds.Intersect(owners).Any();
            var sources = changes.Where(x => Owner(x.Before!) && !Owner(x.After!)).ToArray();
            var destinations = changes.Where(x => !Owner(x.Before!) && Owner(x.After!)).ToArray();
            if (sources.Length != 1 || destinations.Length != 1) Fail();
            var source = sources[0]; var destination = destinations[0];
            if (source.UserId == destination.UserId ||
                !source.Before!.RoleIds.Except(owners).SequenceEqual(source.After!.RoleIds) ||
                !destination.Before!.RoleIds.Union(source.Before.RoleIds.Intersect(owners)).Order().SequenceEqual(destination.After!.RoleIds)) Fail();
            changes = [source!, destination!]; kind = "ownership-transferred";
        } else {
            if (changes.Length != 1) Fail(); var change = changes[0];
            if (change.Before is null) kind = "membership-linked";
            else if (change.After is null) kind = "membership-unlinked";
            else {
                var rolesChanged = !change.Before.RoleIds.SequenceEqual(change.After.RoleIds); var statusChanged = change.Before.IsEnabled != change.After.IsEnabled;
                if (!rolesChanged && !statusChanged) return null;
                kind = rolesChanged && statusChanged ? "membership-changed" : statusChanged ? change.After.IsEnabled ? "membership-enabled" : "membership-disabled" :
                    change.Before.RoleIds.Except(change.After.RoleIds).Any() ? change.After.RoleIds.Except(change.Before.RoleIds).Any() ? "roles-replaced" : "roles-revoked" : "roles-granted";
            }
        }
        return new(1, value.EventId, scope! with { }, value.OperationId, value.Sequence, value.OccurredUtc, kind, actor! with { }, Array.AsReadOnly(changes), Array.AsReadOnly(owners));
    }
    private static IdentityMembershipEventChange Change(IdentityMembershipEventChange change)
    {
        if (change is null || change.UserId == Guid.Empty || change.Before is null && change.After is null) Fail();
        var before = State(change!.Before); var after = State(change.After);
        if (before is not null && after is not null && before.Revision == after.Revision &&
            (before.IsEnabled != after.IsEnabled || !before.RoleIds.SequenceEqual(after.RoleIds))) Fail();
        return new(change.UserId, before, after);
    }
    private static IdentityMembershipEventState? State(IdentityMembershipEventState? state)
    { if (state is null) return null; if (state.Revision == Guid.Empty) Fail(); return new(state.Revision, state.IsEnabled, Array.AsReadOnly(Roles(state.RoleIds, 32))); }
    private static Guid[] Roles(IReadOnlyList<Guid> roles, int maximum)
    { if (roles is null || roles.Count > maximum || roles.Any(x => x == Guid.Empty) || roles.Distinct().Count() != roles.Count) Fail(); return roles!.Order().ToArray(); }
    private static bool Empty(Guid? value) => value is null || value == Guid.Empty;
    private static bool Key(string? value) => value is { Length: > 0 and <= 100 } && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.');
    [System.Diagnostics.CodeAnalysis.DoesNotReturn] private static void Fail() => throw new IdentityProviderException("Committed membership event metadata is invalid.");
}
