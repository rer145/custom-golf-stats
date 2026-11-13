using CustomGolfStats.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(CustomGolfStats.Migration.Tests.Infrastructure.PostgresFixture))]

namespace CustomGolfStats.Migration.Tests.Infrastructure;

/// <summary>
/// One PostgreSQL 17 container for the whole test assembly. On start it migrates a template database once;
/// <see cref="CreateDatabaseAsync"/> then clones it (<c>CREATE DATABASE ... TEMPLATE</c>), so every test class gets
/// its own fully migrated, empty database in milliseconds.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TemplateDatabase = "cgs_template";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();
    private readonly SemaphoreSlim _createLock = new(1, 1);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteAsync($"CREATE DATABASE \"{TemplateDatabase}\"");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseAppDatabase(ConnectionStringFor(TemplateDatabase, pooling: false))
            .Options;
        await using (var db = new AppDbContext(options, TimeProvider.System))
        {
            await db.Database.MigrateAsync();
        }

        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>Creates a new migrated database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"cgs_{Guid.NewGuid():N}";
        await _createLock.WaitAsync();
        try
        {
            await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDatabase}\"");
        }
        finally
        {
            _createLock.Release();
        }

        return ConnectionStringFor(name, pooling: true);
    }

    public async ValueTask DisposeAsync()
    {
        _createLock.Dispose();
        await _container.DisposeAsync();
    }

    private string ConnectionStringFor(string database, bool pooling) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database, Pooling = pooling }.ConnectionString;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor("postgres", pooling: false));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
