using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using IBeam.Repositories;
using IBeam.Repositories.Interfaces;
using IBeam.Services;
using IBeam.Services.Authorization;
using IBeam.Services.Interfaces;
using IBeam.Services.Messaging;
using IBeam.Services.System;

namespace IBeam.Portal.API
{
    public static class Registrations
    {
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
            services.AddScoped<ISystemAuditService, SystemAuditService>();
            services.AddScoped<IServiceAuthorizationService, ServiceAuthorizationService>();
            services.AddScoped<IApplicationRoleAccessService, ApplicationRoleAccessService>();

            services.AddMemoryCache();

            services.AddScoped<IBaseServices, BaseServices>();

        }

        public static void RegisterRepositories(IServiceCollection services)
        {
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
        }
    }
}