using Azure.Data.Tables;
using IBeam.Management.API.Demo;
using IBeam.Management.Models;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using IBeam.Api.Models;
using IBeam.Identity.Interfaces;
using IBeam.Management.Api;
using IBeam.Management.Identity.Api;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.Secrets.AzureKeyVault;
using IBeam.Management.Services.Audit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Management.API.Hosting;

public static class HostRegistration
{
    public const string CorsPolicy = "ManagementApp";
    public static IServiceCollection AddManagementHost(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ValidateLocalListeners(configuration, environment);
        var demo = configuration.GetValue<bool>("ManagementHost:Demo:Enabled");
        if (demo) DevelopmentDemoDiagnostics.Validate(configuration, environment);
        AddDelivery(services, configuration, environment);
        services.AddIBeamManagementIdentityApi(configuration);
        AddStores(services, configuration);
        var provider = configuration["ManagementHost:Secrets:Provider"];
        if (provider == "DevelopmentFile")
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Development secret storage is only available in Development.");
            var directory = DataDirectory(configuration);
            var protection = services.AddDataProtection().SetApplicationName("IBeam.Management.API")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "protection-keys")));
            if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
            services.AddSingleton<IManagementSecretStore, DevelopmentSecretStore>();
        }
        else if (provider == "KeyVault")
        {
            if (!Uri.TryCreate(configuration["ManagementHost:Secrets:VaultUri"], UriKind.Absolute, out var vault) || vault.Scheme != "https")
                throw new InvalidOperationException("A dedicated HTTPS Key Vault URI is required.");
            services.AddIBeamManagementKeyVaultSecrets(new SecretClient(vault, new DefaultAzureCredential()));
        }
        else throw new InvalidOperationException("Select DevelopmentFile or KeyVault secret storage explicitly.");
        services.AddIBeamManagementConnectionsApi(configuration);
        services.AddIBeamManagementApplicationAccessApi();
        services.AddIBeamManagementApplicationSelectorApi(configuration);
        services.AddIBeamManagementSupportUsersApi();
        services.AddIBeamManagementAudit(configuration);
        if (demo)
        {
            services.AddSingleton<DevelopmentDemoDiagnostics>();
            services.AddScoped<IManagementTargetConnectionProbe>(p => p.GetRequiredService<DevelopmentDemoDiagnostics>());
            services.AddScoped<IApplicationDiagnosticsClient>(p => p.GetRequiredService<DevelopmentDemoDiagnostics>());
        }

        CustomerDemoInstallation.Register(services, configuration, environment);
        LifecyclePortalRegistration.Register(services, configuration);
        var origins = configuration.GetSection("ManagementHost:Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 || origins.Any(x => !Uri.TryCreate(x, UriKind.Absolute, out var uri) ||
            uri.GetLeftPart(UriPartial.Authority) != x || uri.Scheme is not ("http" or "https") ||
            !environment.IsDevelopment() && uri.Scheme != "https"))
            throw new InvalidOperationException("Configure exact frontend origins; HTTPS is required outside Development.");
        services.AddCors(o => o.AddPolicy(CorsPolicy, policy => policy.WithOrigins(origins)
            .WithMethods("GET", "POST", "PATCH", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type", "Accept", "If-Match", "Idempotency-Key").WithExposedHeaders("Location", "ETag")));
        services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(new ApiResponse<object>
            {
                Success = false, TraceId = context.HttpContext.TraceIdentifier,
                Errors = [new ApiError { Code = "Management.InvalidRequest", Message = "The request is invalid." }]
            }));
        services.AddControllers().ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new SupportControllerBoundary()));
        services.AddManagementOpenApi();
        return services;
    }

    internal static string DataDirectory(IConfiguration configuration)
        => Path.GetFullPath(configuration["ManagementHost:LocalDataDirectory"]
            ?? throw new InvalidOperationException("A protected local data directory is required for Development adapters."));

    internal static string StorageConnection(IConfiguration configuration)
        => configuration["ManagementHost:Storage:ConnectionString"]
            ?? throw new InvalidOperationException("Dedicated management application storage is required.");

    internal static string[] TableNames(IConfiguration configuration)
    {
        var prefix = configuration["ManagementHost:Storage:TablePrefix"] ?? "Management";
        if (!System.Text.RegularExpressions.Regex.IsMatch(prefix, "^[A-Za-z][A-Za-z0-9]{0,39}$"))
            throw new InvalidOperationException("A valid management storage prefix is required.");
        return [prefix + "Applications", prefix + "ApplicationCredentials", prefix + "ApplicationAccess", prefix + "Audit"];
    }

    internal static void AddStores(IServiceCollection services, IConfiguration configuration)
    {
        var connection = StorageConnection(configuration);
        var names = TableNames(configuration);
        services.AddIBeamManagementAzureTableApplicationStore(new TableClient(connection, names[0]));
        services.AddScoped<IManagementApplicationCredentialRepository>(_ => new AzureTableManagementApplicationCredentialRepository(new TableClient(connection, names[1])));
        services.AddScoped<IManagementApplicationAccessRepository>(_ => new AzureTableManagementApplicationAccessRepository(new TableClient(connection, names[2])));
        services.AddIBeamManagementAzureTableAuditStore(new TableClient(connection, names[3]));
    }

    internal static void AddDelivery(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var provider = configuration["ManagementHost:Mail:Provider"];
        if (provider == "DevelopmentOutbox")
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("The local mail outbox is only available in Development.");
            _ = DataDirectory(configuration);
            services.AddSingleton<IIdentityCommunicationSender, DevelopmentMailSender>();
        }
        else if (provider == "Smtp")
        {
            var smtp = configuration.GetSection("ManagementHost:Mail:Smtp");
            if (string.IsNullOrWhiteSpace(smtp["Host"]) || !System.Net.Mail.MailAddress.TryCreate(smtp["From"], out _) || smtp.GetValue("Port", 587) is < 1 or > 65535)
                throw new InvalidOperationException("A valid SMTP host, sender and port are required.");
            services.AddTransient<IIdentityCommunicationSender, SmtpIdentitySender>();
        }
        else throw new InvalidOperationException("Select DevelopmentOutbox or Smtp message delivery explicitly.");
    }
    private static void ValidateLocalListeners(IConfiguration configuration, IHostEnvironment environment)
    {
        if (configuration["ManagementHost:Secrets:Provider"] != "DevelopmentFile" && configuration["ManagementHost:Mail:Provider"] != "DevelopmentOutbox") return;
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Local testing adapters require Development.");
        var addresses = (configuration["urls"] ?? "http://localhost:5000").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Concat(configuration.GetSection("Kestrel:Endpoints").GetChildren().Select(x => x["Url"] ?? ""));
        if (addresses.Any(x => !Uri.TryCreate(x, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme is not ("http" or "https")))
            throw new InvalidOperationException("Development adapters require loopback-only listeners.");
    }
}
