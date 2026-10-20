using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed partial class ManagementCustomerIdentityClient
{
    public async Task<IReadOnlyList<ManagementCustomerOnboardingPolicyDto>> ListOnboardingPoliciesAsync(ManagementCustomerIdentityConnection c, CancellationToken ct = default)
    {
        const string authority = ManagementCustomerIdentityOperations.OnboardingRead;
        await Verify(c, authority, ct);
        using var document = await Get(c, authority, "/api/management-identity/enterprise-onboardings/policies", true, ct);
        var rows = Data<ManagementCustomerOnboardingPolicyDto[]>(document.RootElement);
        if (rows is null || rows.Length > 32 || rows.Any(x => x is null || !PolicyKey(x.Key)) ||
            rows.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != rows.Length) throw new ManagementCustomerIdentityResponseException();
        return rows.Select(x => new ManagementCustomerOnboardingPolicyDto(x.Key, x.RequiresInitialization)).ToArray();
    }
    public Task<ManagementIdentityOperationDto> CreateOnboardingAsync(ManagementCustomerIdentityConnection connection, CreateManagementEnterpriseOnboarding request,
        string key, CancellationToken ct = default) => Onboard(connection, null, request, key, ct);
    public Task<ManagementIdentityOperationDto> RetryOnboardingAsync(ManagementCustomerIdentityConnection connection, Guid operationId,
        string key, CancellationToken ct = default) => Onboard(connection, operationId, new RetryManagementEnterpriseOnboarding(), key, ct);
    private async Task<ManagementIdentityOperationDto> Onboard<T>(ManagementCustomerIdentityConnection c, Guid? id, T payload, string key, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        var operation = id is null ? ManagementCustomerIdentityOperations.OnboardingCreate : ManagementCustomerIdentityOperations.OnboardingRetry;
        await Verify(c, operation, ct, true);
        var response = await Send(c, operation, "POST", "/api/management-identity/enterprise-onboardings" +
            (id is Guid value ? "/" + value.ToString("D") + "/retry" : ""), true,
            JsonSerializer.SerializeToUtf8Bytes(payload, Json), key, null, false, ct);
        using var document = response.Document;
        return SafeOperation(Data<ManagementIdentityOperationDto>(document.RootElement), operation, null);
    }
    private static ManagementEnterpriseOnboardingDto SafeOnboarding(ManagementEnterpriseOnboardingDto row, ManagementIdentityOperationDto receipt)
    {
        if (receipt.Operation is not (ManagementCustomerIdentityOperations.OnboardingCreate or ManagementCustomerIdentityOperations.OnboardingRetry) ||
            row.OperationId == Guid.Empty || row.OnboardingId != receipt.ResourceId || row.InviteId == Guid.Empty || row.TenantId == Guid.Empty ||
            receipt.Operation == ManagementCustomerIdentityOperations.OnboardingCreate && row.OperationId != receipt.OperationId ||
            row.Status is not ("in-progress" or "recoverable-failure" or "awaiting-acceptance" or "completed") ||
            row.Step is not ("tenant-creation" or "tenant-extensions" or "extensions-completed" or "consumer-initialization" or "invitation-persisted") ||
            row.DeliveryStatus is not ("pending" or "sending" or "sent" or "failed" or "unknown") ||
            row.Completed != (row.Status == "completed") || row.Completed && (row.TenantId is null || row.Step != "invitation-persisted") ||
            row.ErrorCode is not (null or "tenant-setup-incomplete" or "administrator-role-unavailable" or "setup-recovery-required" or "consumer-initialization-incomplete" or
                "delivery-not-sent" or "delivery-unconfirmed" or "invitation-unavailable") || row.ExpiresUtc <= row.CreatedUtc ||
            row.Step != "tenant-creation" && row.TenantId is null) throw new ManagementCustomerIdentityResponseException();
        return new(row.OperationId, row.OnboardingId, row.TenantId, row.InviteId, row.Status, row.Step, row.DeliveryStatus,
            row.Completed, row.ErrorCode, row.CreatedUtc, row.ExpiresUtc);
    }
}
