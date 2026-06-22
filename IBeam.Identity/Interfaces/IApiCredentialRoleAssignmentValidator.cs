namespace IBeam.Identity.Interfaces;

public interface IApiCredentialRoleAssignmentValidator
{
    Task ValidateAsync(
        Guid tenantId,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<string> roleNames,
        CancellationToken ct = default);
}
