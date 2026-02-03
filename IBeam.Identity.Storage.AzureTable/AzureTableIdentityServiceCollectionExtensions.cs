using Azure.Data.Tables;
using ElCamino.AspNetCore.Identity.AzureTable;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using IBeam.Identity.Core.Tenants;
using IBeam.Identity.Storage.AzureTable.Tenants;
using IBeam.Identity.Storage.AzureTable.Types;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Identity.Storage.AzureTable.Extensions;

public static class AzureTableIdentityServiceCollectionExtensions
{
    public static IdentityBuilder AddIBeamIdentityAzureTableStores(
        this IServiceCollection services,
        IConfiguration configuration,
        string configSectionPath = "IdentityAzureTable:IdentityConfiguration",
        bool createTablesIfNotExist = true)
    {
        var opts = configuration.GetSection(configSectionPath).Get<AzureTableIdentityOptions>()
                   ?? new AzureTableIdentityOptions();

        opts.Validate();

        // IdentityConfiguration no longer holds StorageConnectionString in newer versions. :contentReference[oaicite:4]{index=4}
        var identityConfig = new IdentityConfiguration
        {
            TablePrefix = opts.TablePrefix,
            UserTableName = opts.UserTableName,
            RoleTableName = opts.RoleTableName,
            IndexTableName = opts.IndexTableName
        };

        // TableServiceClient is required by the new AddAzureTableStores overload. :contentReference[oaicite:5]{index=5}
        var tableClient = new TableServiceClient(opts.StorageConnectionString);

        services.AddSingleton(opts);
        services.AddSingleton(identityConfig);
        services.AddSingleton(tableClient);
        services.AddScoped<ITenantMembershipStore, AzureTableTenantMembershipStore>();
        services.AddScoped<ITenantProvisioningService, AzureTableTenantProvisioningService>();


        var builder = services
            .AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;

                // Keep passwords optional; strength rules still apply if password is set.
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<ApplicationRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Explicit generic arg fixes CS0411.
        builder.AddAzureTableStores<IdentityCloudContext>(
            () => identityConfig,
            () => tableClient);

        if (createTablesIfNotExist)
        {
            builder.CreateAzureTablesIfNotExists<IdentityCloudContext>();
        }

        return builder;
    }
}