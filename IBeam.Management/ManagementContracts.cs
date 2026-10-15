using System.Security.Claims;

namespace IBeam.Management;

public sealed record ManagementIdentity(string UserId, Guid ManagementOrganizationId);

/// <summary>Only capabilities implemented by the installed module are advertised.</summary>
public sealed record ManagementDiagnosticsInfo(
    string ApplicationKey,
    string Environment,
    int ProtocolVersion,
    IReadOnlyList<string> Capabilities);

/// <summary>Supplies a principal established by trusted authentication, never request body/header claims.</summary>
public interface IManagementPrincipalProvider
{
    ClaimsPrincipal GetPrincipal();
}

public interface IManagementIdentityService
{
    Task<ManagementIdentity> GetCurrentAsync(CancellationToken ct = default);
}

public interface IManagementDiagnosticsInfoService
{
    Task<ManagementDiagnosticsInfo> GetAsync(CancellationToken ct = default);
}

public sealed class ManagementAccessDeniedException : Exception
{
    public ManagementAccessDeniedException() : base("Management access is not permitted.") { }
}
