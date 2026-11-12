using CustomGolfStats.Api.Data.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Data;

/// <summary>
/// The single EF Core context: Identity (users, claims, logins, tokens; no role tables, since
/// <see cref="Account.Role"/> holds the role) plus every golf table. Names are snake_case and enums are strings.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider timeProvider)
    : IdentityUserContext<Account, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>ASP.NET Core data-protection keys (they protect bearer and refresh tokens), shared by every API instance.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<HandicapEntry> HandicapEntries => Set<HandicapEntry>();

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<Tee> Tees => Set<Tee>();

    public DbSet<TeeVersion> TeeVersions => Set<TeeVersion>();

    public DbSet<TeeHole> TeeHoles => Set<TeeHole>();

    public DbSet<Round> Rounds => Set<Round>();

    public DbSet<HoleResult> HoleResults => Set<HoleResult>();

    public DbSet<ShotEntity> Shots => Set<ShotEntity>();

    public DbSet<CourseFlag> CourseFlags => Set<CourseFlag>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampNewAccounts();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampNewAccounts();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity names its tables explicitly (AspNetUsers, ...), which the snake_case convention doesn't rename.
        builder.Entity<Account>().ToTable("accounts");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("account_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("account_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("account_tokens");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    // Identity creates accounts (register, external sign-in), so CreatedAt is stamped here rather than by each caller.
    private void StampNewAccounts()
    {
        foreach (var entry in ChangeTracker.Entries<Account>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = timeProvider.GetUtcNow();
            }
        }
    }
}
