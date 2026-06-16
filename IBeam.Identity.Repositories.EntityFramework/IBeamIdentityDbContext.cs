using IBeam.Identity.Repositories.EntityFramework.Tenants.Entities;
using IBeam.Identity.Repositories.EntityFramework.Types;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IBeam.Identity.Repositories.EntityFramework.Data;

public class IBeamIdentityDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public IBeamIdentityDbContext(DbContextOptions<IBeamIdentityDbContext> options)
        : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Tenant>(entity =>
        {
            entity.ToTable("IBeamIdentityTenants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(256).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(256);
            entity.Property(x => x.Status).HasMaxLength(64).IsRequired();
        });

        builder.Entity<TenantUser>(entity =>
        {
            entity.ToTable("IBeamIdentityTenantUsers");
            entity.HasKey(x => new { x.TenantId, x.UserId });
            entity.Property(x => x.Status).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RolesCsv).HasMaxLength(2048);

            entity.HasOne(x => x.Tenant)
                .WithMany()
                .HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
