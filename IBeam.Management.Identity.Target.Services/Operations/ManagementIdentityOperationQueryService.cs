using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Operations;

/// <summary>Read-only projection; never resumes a workflow or delivers mail.</summary>
public sealed class ManagementIdentityOperationQueryService(IManagementIdentityOperationService receipts,
    IManagementIdentityOperationLookupService lookup, IServiceProvider provider, IServiceOperationExecutor operations,
    IOptions<ManagementIdentityOperationOptions> options) : IManagementIdentityOperationQueryService
{
    public Task<ManagementIdentityOperationDto> GetAsync(Guid id, string authority, CancellationToken ct = default)
        => Run(async token => await Enrich(await receipts.GetAsync(id, authority, token), authority, token), ct);
    public Task<ManagementIdentityOperationDto> ResolveAsync(ManagementIdentityOperationLookup request, string key, string authority, CancellationToken ct = default)
        => Run(async token => await Enrich(await lookup.ResolveAsync(request, key, authority, token), authority, token), ct);
    private async Task<ManagementIdentityOperationDto> Enrich(ManagementIdentityOperationDto receipt, string authority, CancellationToken ct)
    {
        if (receipt.Operation is not (ManagementCustomerIdentityOperations.OnboardingCreate or ManagementCustomerIdentityOperations.OnboardingRetry)) return receipt;
        var onboarding = provider.GetService<IManagementEnterpriseOnboardingService>();
        if (onboarding is null) return receipt;
        try { return receipt with { Onboarding = await onboarding.GetAsync(receipt.ResourceId, authority, ct) }; }
        catch (ManagementCustomerIdentityNotFoundException) { return receipt; } // Setup can be reserved but not yet have its journal.
    }
    private Task<ManagementIdentityOperationDto> Run(Func<CancellationToken, Task<ManagementIdentityOperationDto>> run, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds)); return await run(timeout.Token); }, new ServiceOperationExecutionOptions { OperationName = "management.identity.operations.read",
            PermissionEnabled = false, AuditEnabled = true }, ct);
}
