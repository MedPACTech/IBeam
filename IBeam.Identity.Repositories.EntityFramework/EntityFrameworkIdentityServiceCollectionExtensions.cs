using IBeam.Identity.Interfaces;
using IBeam.Identity.Repositories.EntityFramework.Data;
using IBeam.Identity.Repositories.EntityFramework.Options;
using IBeam.Identity.Repositories.EntityFramework.Tenants;
using IBeam.Identity.Repositories.EntityFramework.Types;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Identity.Repositories.EntityFramework.Extensions;

public static class EntityFrameworkIdentityServiceCollectionExtensions
{
    public static IdentityBuilder AddIBeamIdentityEntityFrameworkStores(
        this IServiceCollection services,
        IConfiguration configuration,
        string configSectionPath = "IdentityEf")
    {
        var bound = configuration.GetSection(configSectionPath).Get<EntityFrameworkIdentityOptions>()
                    ?? new EntityFrameworkIdentityOptions();

        var opts = new EntityFrameworkIdentityOptions
        {
            Provider = bound.Provider,
            MigrationsAssembly = bound.MigrationsAssembly,
            ConnectionString = ResolveConnectionString(configuration, configSectionPath, bound.ConnectionString)
        };

        opts.Validate();

        services.AddSingleton(opts);

        services.AddScoped<ITenantMembershipStore, EntityFrameworkTenantMembershipStore>();

        services.AddDbContext<IBeamIdentityDbContext>(db =>
        {
            switch (opts.Provider)
            {
                case EfProvider.Sqlite:
                    db.UseSqlite(opts.ConnectionString, o =>
                    {
                        if (!string.IsNullOrWhiteSpace(opts.MigrationsAssembly))
                            o.MigrationsAssembly(opts.MigrationsAssembly);
                    });
                    break;

                case EfProvider.SqlServer:
                    throw new InvalidOperationException(
                        "EF Provider SqlServer selected, but SqlServer provider is not included in this package yet.");

                case EfProvider.Postgres:
                    throw new InvalidOperationException(
                        "EF Provider Postgres selected, but Postgres provider is not included in this package yet.");

                default:
                    throw new InvalidOperationException($"Unsupported EF provider: {opts.Provider}");
            }
        });

        var builder = services
            .AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<IBeamIdentityDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        return builder;
    }

    private static string ResolveConnectionString(IConfiguration configuration, string configSectionPath, string? scopedConnectionString)
    {
        // Precedence:
        // 1) {configSectionPath}:ConnectionString (bound into options; default section is "IdentityEf")
        // 2) IBeam:Identity:EntityFramework:ConnectionString
        // 3) IBeam:Repositories:EntityFramework:ConnectionString
        // 4) IBeam:Repositories:ConnectionString
        // 5) IBeam:ConnectionString
        // 6) ConnectionStrings:IdentityEf
        // 7) ConnectionStrings:IdentityEntityFramework
        // 8) ConnectionStrings:IBeam
        // 9) ConnectionStrings:DefaultConnection
        var resolved = FirstNonEmpty(
            scopedConnectionString,
            configuration["IBeam:Identity:EntityFramework:ConnectionString"],
            configuration["IBeam:Repositories:EntityFramework:ConnectionString"],
            configuration["IBeam:Repositories:ConnectionString"],
            configuration["IBeam:ConnectionString"],
            configuration.GetConnectionString("IdentityEf"),
            configuration.GetConnectionString("IdentityEntityFramework"),
            configuration.GetConnectionString("IBeam"),
            configuration.GetConnectionString("DefaultConnection"));

        if (string.IsNullOrWhiteSpace(resolved))
            throw new InvalidOperationException(
                $"Entity Framework Identity connection string is required. Set {configSectionPath}:ConnectionString, " +
                "or IBeam:Identity:EntityFramework:ConnectionString, or IBeam:Repositories:EntityFramework:ConnectionString, " +
                "or IBeam:Repositories:ConnectionString, or IBeam:ConnectionString, or ConnectionStrings:IdentityEf/IdentityEntityFramework/IBeam/DefaultConnection.");

        return resolved!;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
