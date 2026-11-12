using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Api.Data;

public static class AppDbContextOptions
{
    /// <summary>The one place the provider and naming convention are chosen (API, design-time tools and tests).</summary>
    public static DbContextOptionsBuilder UseAppDatabase(this DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();

    /// <inheritdoc cref="UseAppDatabase(DbContextOptionsBuilder, string)"/>
    public static DbContextOptionsBuilder<AppDbContext> UseAppDatabase(this DbContextOptionsBuilder<AppDbContext> options, string connectionString)
    {
        ((DbContextOptionsBuilder)options).UseAppDatabase(connectionString);
        return options;
    }
}
