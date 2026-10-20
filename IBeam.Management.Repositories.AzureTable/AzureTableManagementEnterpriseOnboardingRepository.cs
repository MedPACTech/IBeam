using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.CustomerIdentity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

public sealed class AzureTableManagementEnterpriseOnboardingRepository(TableClient table) : IManagementEnterpriseOnboardingRepository
{
    private static string Partition(ManagementIdentityOperationScope s) => $"{s.IdentityRealmId:N}|{s.OrganizationId:N}|{s.ApplicationId:N}";
    private static TableEntity Encode(ManagementEnterpriseOnboardingEntity row) => new(Partition(row.Scope), row.OnboardingId.ToString("N")) {
        ["Metadata"] = JsonSerializer.Serialize(row with { Version = "" }), ["EncryptedInput"] = row.EncryptedInput };
    public async Task<ManagementEnterpriseOnboardingEntity?> GetAsync(ManagementIdentityOperationScope scope, Guid id, CancellationToken ct = default)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(Partition(scope), id.ToString("N"), cancellationToken: ct);
        if (!response.HasValue) return null;
        var row = JsonSerializer.Deserialize<ManagementEnterpriseOnboardingEntity>(response.Value.GetString("Metadata"))
            ?? throw new InvalidOperationException("Onboarding metadata is invalid.");
        if (row.Scope != scope || row.OnboardingId != id) throw new InvalidOperationException("Onboarding metadata scope is invalid.");
        return row with { EncryptedInput = response.Value.GetString("EncryptedInput") ?? "", Version = response.Value.ETag.ToString() };
    }
    public async Task<bool> TryCreateAsync(ManagementEnterpriseOnboardingEntity row, CancellationToken ct = default)
    {
        try { await table.AddEntityAsync(Encode(row), ct); return true; }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }
    }
    public async Task<ManagementEnterpriseOnboardingEntity> ReplaceAsync(ManagementEnterpriseOnboardingEntity row, string version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version) || version == "*") throw new ManagementIdentityOperationConflictException();
        try {
            var result = await table.UpdateEntityAsync(Encode(row), new ETag(version), TableUpdateMode.Replace, ct);
            return row with { Version = result.Headers.ETag?.ToString() ?? throw new InvalidOperationException("Onboarding version is unavailable.") };
        } catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new ManagementIdentityOperationConflictException(); }
    }
}
public static class ManagementEnterpriseOnboardingStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableEnterpriseOnboardingStore(this IServiceCollection services, TableClient dedicatedTable)
    {
        ArgumentNullException.ThrowIfNull(dedicatedTable);
        services.TryAddScoped<IManagementEnterpriseOnboardingRepository>(_ => new AzureTableManagementEnterpriseOnboardingRepository(dedicatedTable));
        return services;
    }
}
