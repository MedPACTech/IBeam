using IBeam.Identity.Services.Tenants;
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
        var opts = configuration.GetSection(configSectionPath).Get<EntityFrameworkIdentityOptions>()
                   ?? new EntityFrameworkIdentityOptions();

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
                    // You can support this later by adding the SqlServer provider package to this project:
                    // Microsoft.EntityFrameworkCore.SqlServer
                    throw new InvalidOperationException(
                        "EF Provider SqlServer selected, but SqlServer provider is not included in this package yet.");

                case EfProvider.Postgres:
                    // Same idea as SqlServer: add Npgsql.EntityFrameworkCore.PostgreSQL to support.
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
}
