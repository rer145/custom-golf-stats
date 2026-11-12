using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CustomGolfStats.Api.Data;

/// <summary>Used by <c>dotnet ef</c> only. Adding a migration never connects; set ConnectionStrings__Postgres to update a database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=custom_golf_stats;Username=postgres";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(connectionString).Options;
        return new AppDbContext(options, TimeProvider.System);
    }
}
