using IBeam.Management.CustomerIdentity;
using IBeam.Management.Operations;
using IBeam.Management.Services.Credentials;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Services;

public sealed class ManagementOperationTargetOptions
{
    public const string SectionName = "IBeam:Management:Operations:Target";
    public string[] EnabledOperations { get; set; } = [];
}

/// <summary>Read-only discovery preflight. Does not invoke handlers or authorize a human operation.</summary>
public sealed class ManagementOperationCapabilityService(ManagementOperationRegistry registry,
    IManagementPrincipalProvider principals, IManagementApiCredentialRepository credentials,
    IOptions<ManagementDiagnosticsOptions> deployment, IOptions<ManagementOperationTargetOptions> options,
    IServiceProvider provider, IServiceOperationExecutor operations) : IManagementOperationCapabilityService
{
    public Task<IReadOnlyList<ManagementOperationCapability>> GetAsync(CancellationToken ct = default)
        => operations.ExecuteAsync<IReadOnlyList<ManagementOperationCapability>>(this, async token =>
        {
            var principal = principals.GetPrincipal();
            var infoOperation = principal.HasClaim(ManagementClaimTypes.Permission, ManagementOperations.DiagnosticsInfo)
                ? ManagementOperations.DiagnosticsInfo : ManagementCustomerIdentityOperations.Info;
            var identity = ManagementPrincipalGuard.RequireIdentity(principal, ManagementSubjectTypes.Credential, infoOperation);
            var configured = deployment.Value;
            var id = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.CredentialId);
            if (ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.ApplicationKey) != configured.ApplicationKey ||
                ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.Environment) != configured.Environment)
                throw new ManagementAccessDeniedException();
            var row = await credentials.GetAsync(configured.ApplicationKey, configured.Environment, id, token);
            if (row is null || row.CredentialId != id || row.ApplicationKey != configured.ApplicationKey || row.Environment != configured.Environment ||
                row.Status != "Active" || row.RevokedUtc.HasValue || row.ExpiresUtc <= DateTimeOffset.UtcNow ||
                row.Version != ManagementPrincipalGuard.RequireSingle(identity, ManagementClaimTypes.CredentialVersion) ||
                !registry.ValidCapabilities(row.Capabilities) || !row.Capabilities.Contains(infoOperation, StringComparer.Ordinal))
                throw new ManagementAccessDeniedException();
            return registry.Capabilities(row.Capabilities, options.Value.EnabledOperations,
                type => provider.GetService(type) is { } instance && type.IsInstanceOfType(instance));
        }, new ServiceOperationExecutionOptions { OperationName = "management.operations.info", AuditAction = "management.operations.info",
            EntityName = "ManagementOperationCapabilities", PermissionEnabled = false, AuditEnabled = true }, ct);
}

public static class ManagementOperationCapabilityServiceExtensions
{
    public static IServiceCollection AddIBeamManagementOperationCapabilities(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementCredentialValidation(configuration);
        services.AddOptions<ManagementOperationTargetOptions>().Bind(configuration.GetSection(ManagementOperationTargetOptions.SectionName))
            .Validate<ManagementOperationRegistry>((o, registry) => o.EnabledOperations is not null &&
                o.EnabledOperations.Length <= registry.Operations.Count && o.EnabledOperations.All(registry.IsAssignable) &&
                o.EnabledOperations.Distinct(StringComparer.Ordinal).Count() == o.EnabledOperations.Length,
                "Operations must be distinct explicitly registered names.").ValidateOnStart();
        services.TryAddScoped<IManagementOperationCapabilityService, ManagementOperationCapabilityService>();
        return services;
    }
}
