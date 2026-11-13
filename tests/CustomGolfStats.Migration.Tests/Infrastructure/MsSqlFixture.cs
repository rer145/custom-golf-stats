using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Images;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(CustomGolfStats.Migration.Tests.Infrastructure.MsSqlFixture))]

namespace CustomGolfStats.Migration.Tests.Infrastructure;

/// <summary>
/// One SQL Server 2022 container (linux/amd64, emulated on Apple silicon) for the whole test assembly, with a random
/// SA password generated here. <see cref="CreateDatabaseAsync"/> creates a new golftrack database from
/// <c>docs/reference/golftrack/01-tables.sql</c> (plus the columns the production database has beyond that script), so
/// every test gets its own empty golftrack.
/// </summary>
public sealed partial class MsSqlFixture : IAsyncLifetime
{
    private const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

    /// <summary>Columns the production golftrack database has that the reference script lacks.</summary>
    private const string ProductionColumns = """
        ALTER TABLE [dbo].[Course] ADD [Latitude] [decimal](18, 10) NULL, [Longitude] [decimal](18, 10) NULL;
        """;

    // The image starts slowly under emulation.
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(10);

    private readonly MsSqlContainer _container = new MsSqlBuilder(new DockerImage(Image, new Platform("linux/amd64")))
        .WithPassword(GeneratePassword())
        .Build();

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartTimeout);
        await _container.StartAsync(timeout.Token);
    }

    /// <summary>Creates a new golftrack database with the full schema and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"golftrack_{Guid.NewGuid():N}";
        await using (var master = new SqlConnection(ConnectionStringFor("master")))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{name}]", master);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = ConnectionStringFor(name);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "golftrack", "01-tables.sql"));
        foreach (var batch in SplitBatches(script).Append(ProductionColumns))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }

        return connectionString;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <summary>Splits a script on lines that hold only <c>GO</c>, as sqlcmd does; empty batches are dropped.</summary>
    public static IEnumerable<string> SplitBatches(string script) =>
        GoLine().Split(script).Where(batch => !string.IsNullOrWhiteSpace(batch));

    private string ConnectionStringFor(string database) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database }.ConnectionString;

    /// <summary>A random password meeting SQL Server's complexity rules (upper, lower, digit and symbol).</summary>
    private static string GeneratePassword() =>
        "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();
}
