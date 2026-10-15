using AlaraERP.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Infrastructure.Persistence;

public class AlaraDbContext(DbContextOptions<AlaraDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureIdentity(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AlaraDbContext).Assembly);
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<AppUser>(e =>
        {
            e.ToTable("users", Schemas.Identity);
            e.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            e.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            e.Property(u => u.IsActive).HasDefaultValue(true);
            e.HasIndex(u => u.IsActive);
            e.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ux_users_normalized_user_name").IsUnique();
            // Identity checks email uniqueness in code; the unique index makes it race-safe.
            e.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ux_users_normalized_email").IsUnique();
        });
        builder.Entity<AppRole>(e =>
        {
            e.ToTable("roles", Schemas.Identity);
            e.Property(r => r.Description).HasMaxLength(500).IsRequired();
            e.HasIndex(r => r.NormalizedName).HasDatabaseName("ux_roles_normalized_name").IsUnique();
        });
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles", Schemas.Identity);
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", Schemas.Identity);
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims", Schemas.Identity);
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", Schemas.Identity);
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", Schemas.Identity);
    }
}
