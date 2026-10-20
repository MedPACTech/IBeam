using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Management.Identity.Target.Services.Operations;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Onboarding;

public sealed class ManagementEnterpriseOnboardingCommandService(IManagementEnterpriseOnboardingService onboarding,
    IManagementIdentityOperationQueryService queries, IManagementCustomerIdentityTargetAccessService access,
    IServiceOperationExecutor operations, IOptions<ManagementIdentityOperationOptions> options) : IManagementEnterpriseOnboardingCommandService
{
    public Task<ManagementIdentityOperationDto> CreateAsync(CreateManagementEnterpriseOnboarding request, string key, CancellationToken ct = default)
        => Run(ManagementCustomerIdentityOperations.OnboardingCreate, key, async token => {
            var result = await onboarding.CreateAsync(request, key, token);
            return await queries.GetAsync(result.OperationId, ManagementCustomerIdentityOperations.OnboardingCreate, token);
        }, ct);
    public Task<ManagementIdentityOperationDto> RetryAsync(Guid operationId, string key, CancellationToken ct = default)
        => Run(ManagementCustomerIdentityOperations.OnboardingRetry, key, async token => {
            var original = await queries.GetAsync(operationId, ManagementCustomerIdentityOperations.OnboardingRetry, token);
            if (original.Operation is not (ManagementCustomerIdentityOperations.OnboardingCreate or ManagementCustomerIdentityOperations.OnboardingRetry)) throw new ManagementAccessDeniedException();
            if (original.Onboarding?.Completed == true) return original;
            await onboarding.RetryAsync(original.ResourceId, key, token);
            return await queries.ResolveAsync(new(ManagementCustomerIdentityOperations.OnboardingRetry, original.ResourceId, original.ResourceId), key,
                ManagementCustomerIdentityOperations.OnboardingRetry, token);
        }, ct);
    private Task<ManagementIdentityOperationDto> Run(string operation, string key, Func<CancellationToken, Task<ManagementIdentityOperationDto>> run, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            await access.RequireAsync(operation, deadline.Token);
            if (!ManagementDelegationBinding.ValidIdempotencyKey(key)) throw new ManagementCustomerIdentityInvalidRequestException();
            var result = await run(deadline.Token); await access.RequireAsync(operation, deadline.Token); return result;
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = operation, PermissionEnabled = false, AuditEnabled = true }, ct);
}
