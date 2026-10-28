using IBeam.Identity.Interfaces;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable;

public static class IdentityMembershipEventOutboxExtensions
{
    /// <summary>Explicit native atomic event storage. Existing native tables must be provisioned and every writer upgraded.</summary>
    public static IServiceCollection AddIBeamIdentityAzureTableMembershipEvents(this IServiceCollection services)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.TryAddScoped<AzureTableIdentityMembershipEventOutboxStore>();
        services.TryAddScoped<IIdentityMembershipEventOutboxStore>(p => p.GetRequiredService<AzureTableIdentityMembershipEventOutboxStore>());
        services.TryAddScoped<IIdentityTenantMembershipCommittedEventReader>(p => p.GetRequiredService<AzureTableIdentityMembershipEventOutboxStore>());
        services.TryAddScoped<IIdentityMembershipProjectionSource>(p => p.GetRequiredService<AzureTableIdentityMembershipEventOutboxStore>());
        services.AddHostedService<Validate>(); return services;
    }
    private sealed class Registration;
    private sealed class Validate(IOptions<AzureTableIdentityOptions> options) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            var o = options.Value;
            try {
                _ = IdentityMembershipEventPartition.Scope(o, Guid.NewGuid());
                if (o.CreateTablesIfNotExists || o.TenantUsersPk(Guid.NewGuid())[..4] != "TEN|") throw new InvalidOperationException();
                var names = new[] { o.FullTableName(o.TenantUsersTableName), o.FullTableName(o.UserTenantsTableName), o.FullTableName(o.TenantsTableName), o.FullTableName(o.TenantRolesTableName) };
                if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new InvalidOperationException();
            } catch { throw new InvalidOperationException("Membership events require explicit scope, canonical distinct pre-provisioned native tables and all upgraded writers."); }
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
