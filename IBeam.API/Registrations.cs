using System;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using IBeam.Services;
using IBeam.Services.Abstractions;

using IBeam.Scaffolding.Services.Authorization;
using IBeam.Scaffolding.Services.Messaging;
using IBeam.Scaffolding.Services.Interfaces;
using IBeam.Scaffolding.Services;
using IBeam.Scaffolding.Services.System;

using IBeam.Scaffolding.Repositories.Interfaces;
using IBeam.Scaffolding.Repositories;

using IBeam.Repositories.Core;
using IBeam.Repositories.OrmLite;
using IBeam.Repositories.AzureTables;

using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.SqlServer;

namespace IBeam.API
{
    public static class Registrations
    {
        // ------------------------------------------------------------
        // SERVICES
        // ------------------------------------------------------------
        public static void RegisterServices(IServiceCollection services)
        {
            services.AddScoped<IApplicationService, ApplicationService>();
            services.AddScoped<IApplicationAccountService, ApplicationAccountService>();
            services.AddScoped<IApplicationRoleService, ApplicationRoleService>();
            services.AddScoped<IEmailService, EmailService>();
            services.AddScoped<IAccountContextService, AccountContextService>();

            services.AddTransient<ITwilioService, TwilioService>();
            services.AddTransient<ISendGridService, SendGridService>();
            services.AddTransient<ILicenseService, LicenseService>();
            services.AddTransient<ITwoFactorService, TwoFactorService>();

            services.AddTransient<IAccountService, AccountService>();
            services.AddTransient<IAccountRoleService, AccountRoleService>();
            services.AddTransient<IAccountGroupService, AccountGroupService>();
            services.AddTransient<IAccountGroupMemberService, AccountGroupMemberService>();
            services.AddTransient<IAccountGroupRoleService, AccountGroupRoleService>();

            services.AddTransient<IDocumentService, DocumentService>();
            services.AddTransient<IDocumentGenerationService, DocumentGenerationService>();

            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IServiceAuthorizationService, ServiceAuthorizationService>();
            services.AddScoped<IApplicationRoleAccessService, ApplicationRoleAccessService>();

            services.AddScoped<IBaseServices, BaseServices>();

            services.AddMemoryCache();
        }

        // ------------------------------------------------------------
        // REPOSITORIES
        // ------------------------------------------------------------
        public static void RegisterRepositories(
            IServiceCollection services,
            IConfiguration configuration)
        {
            // ------------------------------------------------------------
            // Existing Scaffolding Repositories (legacy)
            // ------------------------------------------------------------
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IApplicationRepository, ApplicationRepository>();
            services.AddScoped<IApplicationAccountRepository, ApplicationAccountRepository>();
            services.AddScoped<IApplicationRoleRepository, ApplicationRoleRepository>();
            services.AddScoped<ILicenseRepository, LicenseRepository>();
            services.AddScoped<IAccountRepository, AccountRepository>();

            services.AddTransient<IAccountRoleRepository, AccountRoleRepository>();
            services.AddTransient<IAccountGroupRepository, AccountGroupRepository>();
            services.AddTransient<IAccountGroupMemberRepository, AccountGroupMemberRepository>();
            services.AddTransient<IAccountGroupRoleRepository, AccountGroupRoleRepository>();

            services.AddScoped<IDocumentRepository, DocumentRepository>();
            services.AddScoped<INotificationRepository, NotificationRepository>();
            services.AddScoped<ISystemAuditRepository, SystemAuditRepository>();
            services.AddScoped<IApplicationRoleAccessRepository, ApplicationRoleAccessRepository>();
            services.AddScoped<IAccountContextRepository, AccountContextRepository>();

            // ------------------------------------------------------------
            // IBeam.Repositories.Core prerequisites
            // ------------------------------------------------------------
            services.AddMemoryCache();

            services.AddScoped<ITenantContext, TenantContext>();

            services.AddSingleton(new RepositoryOptions
            {
                EnableCache = true,
                IdGeneratedByRepository = false,
                DisableSoftDelete = false
            });

            // ------------------------------------------------------------
            // Repository Provider Switch
            // ------------------------------------------------------------
            var provider =
                configuration["IBeam:RepositoryProvider"]
                ?? "OrmLite";

            if (provider.Equals("AzureTables", StringComparison.OrdinalIgnoreCase))
            {
                services.ConfigureIBeamAzureTables(o =>
                {
                    o.ConnectionString =
                        configuration.GetConnectionString("AzureTables");

                    o.TableNamePrefix = "ibeam";
                    o.CreateTablesIfNotExists = true;
                });

                services.AddIBeamAzureTablesRepositories();
            }
            else
            {
                services.AddSingleton<IDbConnectionFactory>(_ =>
                {
                    var connString =
                        configuration.GetConnectionString("DefaultConnection");

                    return new OrmLiteConnectionFactory(
                        connString,
                        SqlServerDialect.Provider);
                });

                services.AddIBeamOrmLiteRepositories();
            }
        }
    }
}
