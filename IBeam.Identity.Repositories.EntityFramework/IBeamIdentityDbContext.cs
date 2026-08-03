using IBeam.Identity.Repositories.EntityFramework.Tenants.Entities;
using IBeam.Identity.Repositories.EntityFramework.Types;
using IBeam.Identity.Repositories.EntityFramework.OAuth.Entities;
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
    public DbSet<OAuthClientEntity> OAuthClients => Set<OAuthClientEntity>();
    public DbSet<OAuthAuthorizationCodeEntity> OAuthAuthorizationCodes => Set<OAuthAuthorizationCodeEntity>();
    public DbSet<OAuthConsentEntity> OAuthConsents => Set<OAuthConsentEntity>();

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

        builder.Entity<OAuthClientEntity>(entity =>
        {
            entity.ToTable("IBeamIdentityOAuthClients");
            entity.HasKey(x => x.ClientId);
            entity.Property(x => x.ClientId).HasMaxLength(200);
            entity.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ClientType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ClientSecretHashAlgorithm).HasMaxLength(64);
            entity.HasIndex(x => x.TenantId);
        });

        builder.Entity<OAuthAuthorizationCodeEntity>(entity =>
        {
            entity.ToTable("IBeamIdentityOAuthAuthorizationCodes");
            entity.HasKey(x => x.CodeHash);
            entity.Property(x => x.CodeHash).HasMaxLength(200);
            entity.Property(x => x.ClientId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RedirectUri).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.Resource).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.CodeChallenge).HasMaxLength(256).IsRequired();
            entity.Property(x => x.CodeChallengeMethod).HasMaxLength(16).IsRequired();
            entity.HasIndex(x => x.ExpiresUtcTicks);
            entity.HasIndex(x => new { x.ClientId, x.TenantId });
        });

        builder.Entity<OAuthConsentEntity>(entity =>
        {
            entity.ToTable("IBeamIdentityOAuthConsents");
            entity.HasKey(x => x.ConsentId);
            entity.Property(x => x.LookupKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ClientId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Resource).HasMaxLength(2048).IsRequired();
            entity.HasIndex(x => x.LookupKey).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId });
        });
    }
}
