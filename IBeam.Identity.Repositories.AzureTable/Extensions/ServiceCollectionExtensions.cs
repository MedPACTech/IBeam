using Azure.Data.Tables;
using ElCamino.AspNetCore.Identity.AzureTable;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Schema;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Schema;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Identity.Repositories.AzureTable.Tenants;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace IBeam.Identity.Repositories.AzureTable.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers ONLY AzureTable provider services/stores + schema management.
        /// Does NOT register Core orchestration services.
        /// </summary>
        public static IServiceCollection AddIBeamIdentityAzureTable(this IServiceCollection services, IConfiguration configuration)
        {
            // Options (bind + validate)
            services.AddOptions<AzureTableIdentityOptions>()
                .Bind(configuration.GetSection(AzureTableIdentityOptions.SectionName))
                .PostConfigure(o =>
                {
                    o.StorageConnectionString = ResolveConnectionString(configuration, o.StorageConnectionString);
                })
                .Validate(o =>
                {
                    o.Validate();
                    return true;
                })
                .ValidateOnStart();

            // Capture and validate once to avoid recursive DI resolution in ElCamino store delegates.
            var opts = configuration
                .GetSection(AzureTableIdentityOptions.SectionName)
                .Get<AzureTableIdentityOptions>() ?? new AzureTableIdentityOptions();
            opts.StorageConnectionString = ResolveConnectionString(configuration, opts.StorageConnectionString);
            opts.Validate();

            var tableClient = new TableServiceClient(opts.StorageConnectionString);
            var identityConfig = new IdentityConfiguration
            {
                TablePrefix = opts.TablePrefix,
                IndexTableName = opts.IndexTableName,
                UserTableName = opts.UserTableName,
                RoleTableName = opts.RoleTableName,
            };

            services.AddSingleton(tableClient);
            services.AddSingleton(identityConfig);

            // ElCamino context (scoped)
            services.AddScoped<IdentityCloudContext>(sp =>
            {
                var cfg = sp.GetRequiredService<IdentityConfiguration>();
                var client = sp.GetRequiredService<TableServiceClient>();
                return new IdentityCloudContext(cfg, client);
            });

            // Provider-internal Microsoft Identity wiring
            var identityBuilder = services
                .AddIdentityCore<ApplicationUser>(options =>
                {
                    // keep empty for now; later bind password/lockout/token options here
                })
                .AddRoles<ApplicationRole>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            // Hook ElCamino Azure Table stores
            identityBuilder.AddAzureTableStores<IdentityCloudContext>(
                _ => identityConfig,
                _ => tableClient);

            // Abstractions stores (scoped)
            services.AddScoped<UserStore<ApplicationUser, ApplicationRole, IdentityCloudContext>>();
            services.AddScoped<IIdentityUserStore, AzureTableIdentityUserStore>();
            services.AddScoped<ITenantMembershipStore, AzureTableTenantMembershipStore>();
            services.AddScoped<IOtpChallengeStore, AzureTableOtpChallengeStore>();
            services.AddScoped<ITenantProvisioningService, AzureTableTenantProvisioningService>();
            services.AddScoped<IExternalLoginStore, AzureTableExternalLoginStore>();
            services.AddScoped<IAuthSessionStore, AzureTableAuthSessionStore>();

            // Schema manager + startup ensure
            services.AddScoped<IIdentitySchemaManager, AzureTableIdentitySchemaManager>();
            services.AddHostedService<AzureTableIdentitySchemaHostedService>();

            return services;
        }

        private static string ResolveConnectionString(IConfiguration configuration, string? scopedConnectionString)
        {
            // Precedence:
            // 1) IBeam:Identity:AzureTable:StorageConnectionString (bound into options)
            // 2) IBeam:AzureTables
            // 3) IBeam:ConnectionString
            // 4) ConnectionStrings:AzureTables
            // 5) ConnectionStrings:AzureStorage
            // 6) ConnectionStrings:IBeam
            // 7) ConnectionStrings:DefaultConnection
            var resolved =
                FirstNonEmpty(
                    scopedConnectionString,
                    configuration["IBeam:AzureTables"],
                    configuration["IBeam:ConnectionString"],
                    configuration.GetConnectionString("AzureTables"),
                    configuration.GetConnectionString("AzureTable"),
                    configuration.GetConnectionString("AzureStorage"),
                    configuration.GetConnectionString("IBeam"),
                    configuration.GetConnectionString("DefaultConnection"),
                    configuration.GetConnectionString("IdentityAzureTable"));

            if (string.IsNullOrWhiteSpace(resolved))
                throw new InvalidOperationException(
                    "AzureTable Identity connection string is required. Set IBeam:Identity:AzureTable:StorageConnectionString, " +
                    "or IBeam:AzureTables, or IBeam:ConnectionString, or ConnectionStrings:AzureTables/AzureStorage/IBeam/DefaultConnection.");

            return resolved!;
        }

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
    }
}
