using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Onboarding;

public sealed class ConfiguredManagementCustomerOnboardingPolicy(IOptions<ManagementEnterpriseOnboardingOptions> options) : IManagementCustomerOnboardingPolicy
{
    public Task<IReadOnlyList<ManagementCustomerOnboardingPolicy>> GetEnabledAsync(CancellationToken ct = default)
    {
        var value = options.Value;
        if (!value.IsValid()) throw new ManagementCustomerIdentityUnavailableException();
        return Task.FromResult<IReadOnlyList<ManagementCustomerOnboardingPolicy>>(value.Policies
            .Where(x => !value.DisabledPolicyKeys.Contains(x.Key)).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new ManagementCustomerOnboardingPolicy(x.Key, x.Value, value.Initializers.GetValueOrDefault(x.Key))).ToArray());
    }
}
internal static class OnboardingPolicyValidation
{
    public static IReadOnlyList<ManagementCustomerOnboardingPolicy> Validate(IReadOnlyList<ManagementCustomerOnboardingPolicy> rows)
    {
        if (rows is not { Count: <= 32 } || rows.Any(x => x is null || !ManagementEnterpriseOnboardingOptions.ValidKey(x.Key) ||
            !Provisioning.ManagementCustomerProvisioningOptions.ValidRoleName(x.InitialAdministratorRole) ||
            x.InitializerKey is not null && !ManagementEnterpriseOnboardingOptions.ValidKey(x.InitializerKey)) ||
            rows.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != rows.Count)
            throw new ManagementCustomerIdentityUnavailableException();
        return rows;
    }
}
public sealed class ManagementCustomerOnboardingPolicyCatalog(IManagementCustomerOnboardingPolicy policy,
    IManagementCustomerIdentityTargetAccessService access, IServiceOperationExecutor operations) : IManagementCustomerOnboardingPolicyCatalog
{
    public Task<IReadOnlyList<ManagementCustomerOnboardingPolicyDto>> ListAsync(CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token => {
            await access.RequireAsync(ManagementCustomerIdentityOperations.OnboardingRead, token);
            IReadOnlyList<ManagementCustomerOnboardingPolicy> rows;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try { rows = OnboardingPolicyValidation.Validate(await policy.GetEnabledAsync(timeout.Token)); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
            await access.RequireAsync(ManagementCustomerIdentityOperations.OnboardingRead, token);
            return (IReadOnlyList<ManagementCustomerOnboardingPolicyDto>)rows.Select(x => new ManagementCustomerOnboardingPolicyDto(x.Key, x.InitializerKey is not null)).ToArray();
        }, new ServiceOperationExecutionOptions { OperationName = ManagementCustomerIdentityOperations.OnboardingRead,
            AuditAction = ManagementCustomerIdentityOperations.OnboardingRead, PermissionEnabled = false, AuditEnabled = true }, ct);
}
