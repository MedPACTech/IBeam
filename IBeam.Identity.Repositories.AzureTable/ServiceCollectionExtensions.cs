using Azure.Data.Tables;
using ElCamino.AspNetCore.Identity.AzureTable;
using ElCamino.AspNetCore.Identity.AzureTable.Model;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Schema;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
            services.AddOptions<AzureTableIdentityOptions>()
                .Bind(configuration.GetSection("IBeam:Identity:AzureTable"))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // Provider core primitives
            services.AddSingleton<TableServiceClient>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<AzureTableIdentityOptions>>().Value;
                return new TableServiceClient(opts.StorageConnectionString);
            });

            services.AddSingleton<IdentityConfiguration>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<AzureTableIdentityOptions>>().Value;

                return new IdentityConfiguration
                {
                    StorageConnectionString = opts.StorageConnectionString,
                    TablePrefix = opts.TablePrefix,
                    IndexTableName = opts.IndexTableName,
                    RoleTableName = opts.RolesTableName,
                    UserTableName = opts.UsersTableName,
                };
            });

            // INTERNAL Microsoft Identity wiring (ElCamino)
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Configure Identity options as desired (password, lockout, etc.)
                // This is provider-internal; do not leak types upward.
            })
                .AddRoles<ApplicationRole>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            // Wire ElCamino Azure Table stores
            services.AddTransient<IdentityCloudContext>(sp =>
            {
                var identityConfig = sp.GetRequiredService<IdentityConfiguration>();
                var tableClient = sp.GetRequiredService<TableServiceClient>();
                return new IdentityCloudContext(identityConfig, tableClient);
            });

            services.AddIdentityAzureTableStores();

            // Register provider implementations for Abstractions
            services.AddTransient<IIdentityUserStore, Stores.AzureTableIdentityUserStore>();
            services.AddTransient<ITenantMembershipStore, Stores.AzureTableTenantMembershipStore>();
            services.AddTransient<IOtpChallengeStore, Stores.AzureTableOtpChallengeStore>();

            // Schema manager + startup ensure
            services.AddSingleton<IIdentitySchemaManager, Schema.AzureTableIdentitySchemaManager>();
            services.AddHostedService<Schema.AzureTableIdentitySchemaHostedService>();

            return services;
        }

        /// <summary>
        /// Encapsulates ElCamino store registration so it stays internal.
        /// </summary>
        private static IServiceCollection AddIdentityAzureTableStores(this IServiceCollection services)
        {
            services.AddTransient<IUserStore<ApplicationUser>>(sp =>
            {
                var identityConfig = sp.GetRequiredService<IdentityConfiguration>();
                var tableClient = sp.GetRequiredService<TableServiceClient>();
                var builder = new IdentityBuilder(typeof(ApplicationUser), typeof(ApplicationRole), services);

                // ElCamino expects an AzureTable store hookup via extension methods.
                // This adapter keeps the external DI surface clean.
                builder.AddAzureTableStores<IdentityCloudContext>(
                    () => identityConfig,
                    () => tableClient);

                // Resolve the user store after ElCamino registers it
                return sp.GetRequiredService<IUserStore<ApplicationUser>>();
            });

            // NOTE:
            // If your current provider already calls AddAzureTableStores on the IdentityBuilder
            // (like your existing AddIBeamIdentityAzureTableStores does :contentReference[oaicite:2]{index=2}),
            // remove this method and keep your existing wiring, but keep it provider-internal.

            return services;
        }
    }
}
