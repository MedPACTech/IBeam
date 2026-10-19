using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Management.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Repositories.AzureTable;

public sealed class AzureTableManagementApplicationCredentialRepository(TableClient table) : IManagementApplicationCredentialRepository
{
    private static string Partition(Guid org, Guid app) => "ORG|" + org.ToString("N") + "|APP|" + app.ToString("N");
    public async Task<ManagementApplicationCredentialEntity?> GetAsync(Guid organizationId, Guid applicationId, Guid credentialId, CancellationToken ct = default)
    {
        var response = await table.GetEntityIfExistsAsync<TableEntity>(Partition(organizationId, applicationId), credentialId.ToString("N"), cancellationToken: ct);
        if (!response.HasValue) return null;
        var x = response.Value;
        return new()
        {
            OrganizationId = x.GetGuid("OrganizationId")!.Value, ApplicationId = x.GetGuid("ApplicationId")!.Value,
            CredentialId = x.GetGuid("CredentialId")!.Value, SecretReference = x.GetString("SecretReference"),
            BaseUrl = x.GetString("BaseUrl"), VerifiedUtc = x.GetDateTimeOffset("VerifiedUtc")!.Value, CreatedBy = x.GetString("CreatedBy"),
            Capabilities = JsonSerializer.Deserialize<string[]>(x.GetString("Capabilities")) ?? [],
            CustomerIdentity = x.GetString("CustomerIdentity") is { } info
                ? JsonSerializer.Deserialize<CustomerIdentity.ManagementCustomerIdentityDiscovery>(info) : null
        };
    }
    public async Task CreateAsync(ManagementApplicationCredentialEntity x, CancellationToken ct = default)
        => await table.AddEntityAsync(new TableEntity(Partition(x.OrganizationId, x.ApplicationId), x.CredentialId.ToString("N"))
        {
            ["OrganizationId"] = x.OrganizationId, ["ApplicationId"] = x.ApplicationId, ["CredentialId"] = x.CredentialId,
            ["SecretReference"] = x.SecretReference, ["BaseUrl"] = x.BaseUrl, ["VerifiedUtc"] = x.VerifiedUtc,
            ["CreatedBy"] = x.CreatedBy, ["Capabilities"] = JsonSerializer.Serialize(x.Capabilities),
            ["CustomerIdentity"] = x.CustomerIdentity is null ? null : JsonSerializer.Serialize(x.CustomerIdentity)
        }, ct);
}
public static class ManagementApplicationCredentialStorageExtensions
{
    /// <summary>Explicitly provision ManagementApplicationCredentials beforehand. No customer/global TableClient is replaced.</summary>
    public static IServiceCollection AddIBeamManagementAzureTableApplicationCredentialStore(this IServiceCollection services, TableClient credentialTable)
    {
        ArgumentNullException.ThrowIfNull(credentialTable);
        services.TryAddScoped<IManagementApplicationCredentialRepository>(_ => new AzureTableManagementApplicationCredentialRepository(credentialTable));
        return services;
    }
}
