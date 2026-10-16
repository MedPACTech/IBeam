using IBeam.Management.Services.Credentials;
using IBeam.Management;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services;

public sealed class ManagementDiagnosticsInfoService(
    IManagementPrincipalProvider principals,
    IServiceOperationExecutor operations,
    IOptions<ManagementDiagnosticsOptions> options) : IManagementDiagnosticsInfoService
{
    private readonly IManagementDiagnosticsAccessService? _access;

    public ManagementDiagnosticsInfoService(
        IManagementPrincipalProvider principals, IServiceOperationExecutor operations,
        IOptions<ManagementDiagnosticsOptions> options, IManagementDiagnosticsAccessService access)
        : this(principals, operations, options) => _access = access;

    [IBeamOperation(ManagementOperations.DiagnosticsInfo, Permission = false)]
    public Task<ManagementDiagnosticsInfo> GetAsync(CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            token.ThrowIfCancellationRequested();
            if (_access is not null) await _access.RequireAsync(ManagementOperations.DiagnosticsInfo, token);
            var configured = options.Value;
            var identity = ManagementPrincipalGuard.RequireIdentity(
                principals.GetPrincipal(), ManagementSubjectTypes.Credential, ManagementOperations.DiagnosticsInfo);
            ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.CredentialId);
            if (!string.Equals(ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.ApplicationKey),
                    configured.ApplicationKey, StringComparison.Ordinal) ||
                !string.Equals(ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.Environment),
                    configured.Environment, StringComparison.Ordinal))
                throw new ManagementAccessDeniedException();

            return new ManagementDiagnosticsInfo(
                configured.ApplicationKey, configured.Environment, 1,
                Array.AsReadOnly(new[] { ManagementOperations.DiagnosticsInfo }));
        }, new ServiceOperationExecutionOptions
        {
            OperationName = ManagementOperations.DiagnosticsInfo,
            AuditAction = ManagementOperations.DiagnosticsInfo,
            EntityName = nameof(ManagementDiagnosticsInfo),
            PermissionEnabled = false,
            AuditEnabled = true
        }, ct);
}
