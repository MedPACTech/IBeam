using IBeam.Identity.Interfaces;
using IBeam.Identity.Events;
using IBeam.Identity.Services.Otp;
using IBeam.Identity.Services.ApiCredentials;
using IBeam.Identity.Services.Auth;
using IBeam.Identity.Services.Auth.Attempts;
using IBeam.Identity.Services.Authorization;
using IBeam.Identity.Services.Tenants;
using IBeam.Identity.Services.Tokens;
using IBeam.Identity.Services.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IBeam.Identity.Options;

namespace IBeam.Identity.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IBeam Identity core services and options for DI.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddIBeamIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {

        services.AddOptions<OtpOptions>()
        .Bind(configuration.GetSection(OtpOptions.SectionName))
        .PostConfigure(o =>
        {
            var settingPath = $"{OtpOptions.SectionName}:{nameof(OtpOptions.AllowAutoProvisionForUnknownUser)}";
            var rawSetting = configuration[settingPath];
            if (!string.IsNullOrWhiteSpace(rawSetting))
                return;

            var environmentName =
                configuration["DOTNET_ENVIRONMENT"] ??
                configuration["ASPNETCORE_ENVIRONMENT"] ??
                "Production";

            o.AllowAutoProvisionForUnknownUser = string.Equals(
                environmentName,
                "Development",
                StringComparison.OrdinalIgnoreCase);
        })
        .Validate(o =>
        {
            o.Validate();
            return true;
        })
        .ValidateOnStart();

        services.AddOptions<JwtOptions>()
        .Bind(configuration.GetSection(JwtOptions.SectionName))
        .Validate(o =>
        {
            o.Validate();
            return true;
        })
        .ValidateOnStart();

        services.AddOptions<FeatureOptions>()
        .Bind(configuration.GetSection("IBeam:Identity:Features"));

        services.AddOptions<LoginAttemptOptions>()
            .Bind(configuration.GetSection(LoginAttemptOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddOptions<TenantProvisioningOptions>()
            .Bind(configuration.GetSection(TenantProvisioningOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddOptions<RoleManagementOptions>()
        .Bind(configuration.GetSection(RoleManagementOptions.SectionName));

        services.AddOptions<OAuthOptions>()
        .Bind(configuration.GetSection(OAuthOptions.SectionName));

        services.AddOptions<IdentityEmailTemplateOptions>()
        .Bind(configuration.GetSection(IdentityEmailTemplateOptions.SectionName));

        services.AddOptions<PermissionAccessOptions>()
        .Bind(configuration.GetSection(PermissionAccessOptions.SectionName));

        services.AddOptions<IBeamAccessControlOptions>()
        .Bind(configuration.GetSection(IBeamAccessControlOptions.SectionName));

        services.AddOptions<ApiCredentialOptions>()
        .Bind(configuration.GetSection(ApiCredentialOptions.SectionName))
        .Validate(o =>
        {
            o.Validate();
            return true;
        })
        .ValidateOnStart();

        services.AddIBeamAuthEvents(configuration);

        // Core services
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IIdentityTenantService, IdentityTenantService>();
        services.AddScoped<ITenantSelectionService, TenantSelectionService>();
        services.AddScoped<ITenantRoleService, TenantRoleService>();
        services.TryAddScoped<ITenantMetadataProvider, NoOpTenantMetadataProvider>();
        services.TryAddScoped<ITenantLifecycleHook, NoOpTenantLifecycleHook>();
        services.TryAddScoped<ITenantExtensionCoordinator, NoOpTenantExtensionCoordinator>();
        services.TryAddScoped<IIdentityUserExtensionCoordinator, NoOpIdentityUserExtensionCoordinator>();
        services.TryAddScoped<ITenantInfoResolver, TenantInfoResolver>();
        services.TryAddScoped<IAuthAttemptStore, InMemoryAuthAttemptStore>();
        services.TryAddScoped<IAuthAttemptContextProvider, NoOpAuthAttemptContextProvider>();
        services.AddScoped<IRoleAccessAuthorizer, RoleAccessAuthorizer>();
        services.TryAddScoped<IPermissionAccessStore, NoOpPermissionAccessStore>();
        services.TryAddScoped<IIBeamAccessGrantStore, NoOpAccessGrantStore>();
        services.TryAddScoped<IIBeamAccessCatalogOverrideStore, NoOpAccessCatalogOverrideStore>();
        services.AddScoped<IPermissionGrantResolver, PermissionGrantResolver>();
        services.AddScoped<IPermissionAccessAuthorizer, PermissionAccessAuthorizer>();
        services.AddSingleton<IIBeamOperationCatalogProvider, OperationCatalogProvider>();
        services.AddScoped<IIBeamAccessControlService, IBeamAccessControlService>();
        services.AddSingleton<IPermissionCatalogProvider, PermissionCatalogProvider>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IApiCredentialKeyGenerator, ApiCredentialKeyGenerator>();
        services.AddScoped<IApiCredentialSecretHasher, ApiCredentialSecretHasher>();
        services.AddScoped<IApiCredentialRoleCatalogProvider, ApiCredentialRoleCatalogProvider>();
        services.AddScoped<IApiCredentialRoleAssignmentValidator, ApiCredentialRoleAssignmentValidator>();
        services.AddScoped<IApiCredentialPrincipalFactory, ApiCredentialPrincipalFactory>();
        services.AddScoped<IApiCredentialScopeCatalogProvider, ApiCredentialScopeCatalogProvider>();
        services.AddScoped<IApiCredentialAccessService, ApiCredentialAccessService>();
        services.AddScoped<IApiCredentialService, ApiCredentialService>();
        services.AddScoped<IApiCredentialAuthenticator, ApiCredentialAuthenticator>();


        // Note: IOtpChallengeStore, ISender, and other dependencies must be registered by the consumer or by repository/communications packages.

        return services;
    }

    public static IServiceCollection AddIBeamApiCredentials(
        this IServiceCollection services,
        Action<ApiCredentialOptionsBuilder> configure)
    {
        services.Configure<ApiCredentialOptions>(options =>
        {
            var builder = new ApiCredentialOptionsBuilder(options);
            configure(builder);
        });

        services.TryAddScoped<IApiCredentialScopeCatalogProvider, ApiCredentialScopeCatalogProvider>();
        services.TryAddScoped<IApiCredentialAccessService, ApiCredentialAccessService>();
        return services;
    }

    public static IServiceCollection AddIBeamAccessControl(
        this IServiceCollection services,
        Action<IBeamAccessControlOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);

            var configured = new IBeamAccessControlOptions();
            configure(configured);

            foreach (var providerType in configured.ResourceCatalogProviders.Distinct())
            {
                services.TryAddEnumerable(ServiceDescriptor.Scoped(
                    typeof(IIBeamAccessCatalogProvider),
                    providerType));
            }
        }

        services.TryAddScoped<IIBeamAccessGrantStore, NoOpAccessGrantStore>();
        services.TryAddScoped<IIBeamAccessCatalogOverrideStore, NoOpAccessCatalogOverrideStore>();
        services.TryAddScoped<IApiCredentialScopeCatalogProvider, ApiCredentialScopeCatalogProvider>();
        services.TryAddSingleton<IIBeamOperationCatalogProvider, OperationCatalogProvider>();
        services.TryAddScoped<IIBeamAccessControlService, IBeamAccessControlService>();
        return services;
    }

    public static IServiceCollection AddIBeamIdentityTenantExtension<TTenant, TStore>(
        this IServiceCollection services)
        where TTenant : class, IIdentityTenantExtension
        where TStore : class, ITenantExtensionStore<TTenant>
    {
        services.AddScoped<ITenantExtensionStore<TTenant>, TStore>();
        services.AddScoped<ITenantExtensionResolver<TTenant>, TenantExtensionResolver<TTenant>>();
        services.AddScoped<ITenantExtensionCoordinator, TenantExtensionCoordinator<TTenant>>();

        return services;
    }

    public static IServiceCollection AddIBeamIdentityUserExtension<TUserExtension, TStore>(
        this IServiceCollection services)
        where TUserExtension : class, IIdentityUserExtension
        where TStore : class, IIdentityUserExtensionStore<TUserExtension>
    {
        services.AddScoped<IIdentityUserExtensionStore<TUserExtension>, TStore>();
        services.AddScoped<IIdentityUserExtensionResolver<TUserExtension>, IdentityUserExtensionResolver<TUserExtension>>();
        services.AddScoped<IIdentityUserExtensionCoordinator, IdentityUserExtensionCoordinator<TUserExtension>>();

        return services;
    }

    public static IServiceCollection AddIBeamIdentityTenantMetadataProvider<TProvider>(
        this IServiceCollection services)
        where TProvider : class, ITenantMetadataProvider
    {
        services.AddScoped<ITenantMetadataProvider, TProvider>();
        return services;
    }

    public static IServiceCollection AddIBeamIdentityTenantLifecycleHook<THook>(
        this IServiceCollection services)
        where THook : class, ITenantLifecycleHook
    {
        services.AddScoped<ITenantLifecycleHook, THook>();
        return services;
    }

    public static IServiceCollection AddIBeamIdentityPermissionMappings(
        this IServiceCollection services,
        Action<PermissionAccessMapBuilder> configure)
    {
        var builder = new PermissionAccessMapBuilder();
        configure(builder);

        var entries = builder.Build();
        services.PostConfigure<PermissionAccessOptions>(opts =>
        {
            foreach (var entry in entries)
                opts.Mappings.Add(entry);
        });

        return services;
    }

    public static IServiceCollection AddIBeamIdentityPermissionCatalog(
        this IServiceCollection services,
        Action<PermissionCatalogBuilder> configure)
    {
        var builder = new PermissionCatalogBuilder();
        configure(builder);

        var entries = builder.Build();
        services.PostConfigure<PermissionAccessOptions>(opts =>
        {
            foreach (var entry in entries)
                opts.Catalog.Add(entry);
        });

        return services;
    }

    public static IServiceCollection AddIBeamAuthEvents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AuthEventOptions>()
            .Bind(configuration.GetSection(AuthEventOptions.SectionName));

        services.TryAddScoped<IAuthEventPublisher, NoOpAuthEventPublisher>();
        services.TryAddScoped<IAuthLifecycleHook, NoOpAuthLifecycleHook>();

        return services;
    }

    public static IServiceCollection AddIBeamAuthEvents(
        this IServiceCollection services,
        Action<AuthEventOptions> configure)
    {
        services.Configure(configure);
        services.TryAddScoped<IAuthEventPublisher, NoOpAuthEventPublisher>();
        services.TryAddScoped<IAuthLifecycleHook, NoOpAuthLifecycleHook>();
        return services;
    }

    /// <summary>
    /// Registers IBeam Identity core auth services and options for DI.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddIBeamIdentityAuthPasswordService(this IServiceCollection services)
    {
        // Core services
        services.AddScoped<IIdentityAuthService, PasswordAuthService>();
        return services;
    }

    /// <summary>
    /// Registers IBeam Identity core auth services and options for DI.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddIBeamIdentityAuthOtpService(this IServiceCollection services)
    {
        // Core services
        services.AddScoped<IIdentityOtpAuthService, OtpAuthService>();
        return services;
    }

    public static IServiceCollection AddIBeamIdentityAuthOAuthService(this IServiceCollection services)
    {
        services.AddScoped<IIdentityOAuthAuthService, OAuthAuthService>();
        return services;
    }
}
