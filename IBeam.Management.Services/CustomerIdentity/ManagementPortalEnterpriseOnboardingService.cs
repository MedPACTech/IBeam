using System.Net.Mail;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity.Delegation;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed class ManagementPortalEnterpriseOnboardingService(PortalMutationRuntime runtime, PortalMutationReferences references,
    IManagementCustomerIdentityClient client) : IManagementPortalEnterpriseOnboardingService
{
    public Task<IReadOnlyList<ManagementCustomerOnboardingPolicyDto>> ListPoliciesAsync(Guid app, CancellationToken ct = default)
        => runtime.Run(this, app, "management.portal.customer-identity.onboarding.policies", async token => {
            const string authority = ManagementCustomerIdentityOperations.OnboardingRead;
            await runtime.Authorize(app, authority, token);
            var connection = await runtime.Resolve(app, authority, token);
            IReadOnlyList<ManagementCustomerOnboardingPolicyDto> result;
            using (var attempt = runtime.Attempt(token)) result = await client.ListOnboardingPoliciesAsync(connection, attempt.Token);
            await runtime.Recheck(connection, authority, token); return result;
        }, ct);
    public Task<ManagementPortalOperationResult> CreateAsync(Guid applicationId, CreateManagementEnterpriseOnboarding request, string key, CancellationToken ct = default)
        => Execute(applicationId, null, request, key, ct);
    public Task<ManagementPortalOperationResult> RetryAsync(Guid applicationId, Guid operationId, string key, CancellationToken ct = default)
        => Execute(applicationId, operationId, null, key, ct);
    private Task<ManagementPortalOperationResult> Execute(Guid app, Guid? id, CreateManagementEnterpriseOnboarding? request, string key, CancellationToken ct)
    {
        var operation = id is null ? ManagementCustomerIdentityOperations.OnboardingCreate : ManagementCustomerIdentityOperations.OnboardingRetry;
        var action = "management.portal.customer-identity.onboarding." + (id is null ? "create" : "retry");
        return runtime.Run(this, app, action, async token => {
            await runtime.Authorize(app, operation, token);
            if (!ManagementDelegationBinding.ValidIdempotencyKey(key) || id == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
            if (id is null) Validate(request);
            var connection = await runtime.Resolve(app, operation, token);
            Guid job = Guid.Empty;
            using var attempt = runtime.Attempt(token);
            if (id is Guid original) {
                var receipt = await client.GetOperationAsync(connection, original, operation, attempt.Token);
                if (receipt.Operation is not (ManagementCustomerIdentityOperations.OnboardingCreate or ManagementCustomerIdentityOperations.OnboardingRetry))
                    throw new ManagementAccessDeniedException();
                job = receipt.ResourceId;
            }
            var state = references.Create(new(operation, job, id is null ? null : job), key);
            ManagementIdentityOperationDto? result;
            {
                try { result = id is null ? await client.CreateOnboardingAsync(connection, request!, key, attempt.Token) :
                    await client.RetryOnboardingAsync(connection, id.Value, key, attempt.Token); }
                catch (Exception ex) when (PortalMutationRuntime.Uncertain(ex, token)) { result = null; }
            }
            await runtime.Recheck(connection, operation, token);
            var tracking = references.Result(connection, state, result);
            await runtime.Outcome(this, app, action + "." + tracking.Status, result?.Onboarding?.TenantId, token);
            return tracking;
        }, ct);
    }
    private static void Validate(CreateManagementEnterpriseOnboarding? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.TenantName) || value.TenantName.Length > 512 || value.TenantName.Any(char.IsControl) ||
            value.InitialAdminEmail is null || value.InitialAdminEmail.Length is < 3 or > 320 || value.InitialAdminEmail.Any(char.IsControl) ||
            !MailAddress.TryCreate(value.InitialAdminEmail.Trim(), out var email) || email.Address != value.InitialAdminEmail.Trim() ||
            value.PolicyKey is not { Length: > 0 and <= 64 } || value.PolicyKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) ||
            value.ExternalReference is { } reference && (reference.Length > 128 || reference.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':' or '/'))))
            throw new ManagementCustomerIdentityInvalidRequestException();
    }
}
