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

    /// <summary>
    /// The live database's Round_SeasonSummary (as the controller read it from production), applied after 03-sprocs.sql
    /// so the parity tests run golftrack's real definition.
    /// </summary>
    private const string LiveRoundSeasonSummary = """
        ALTER PROCEDURE [dbo].[Round_SeasonSummary]
            @year int
        AS
        BEGIN
            SET NOCOUNT ON;
            select count(r.Id) as Rounds, 0 as Rounds9, 0 as Rounds18,
                count(case when r.Score < 80 then r.Id end) as RoundsUnder80,
                count(case when r.Score >= 80 and r.Score < 90 then r.Id end) as Rounds8089,
                count(case when r.Score >= 90 then r.Id end) as RoundsOver90
            from [Round] r
            inner join Tee t on t.Id = r.TeeId
            inner join Course c on c.Id = t.CourseId
            inner join Hole h on h.TeeId = t.Id
            inner join [RoundDetail] rd on rd.HoleId = h.Id and rd.RoundId = r.Id
            where datepart(yyyy, r.PlayedOn) = @year and r.IsActive = 1
        END
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

    /// <summary>
    /// Creates a new golftrack database with the full schema (and, when asked, golftrack's stored procedures from
    /// <c>03-sprocs.sql</c> with the live Round_SeasonSummary) and returns its connection string.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(bool withProcedures = false)
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
        var batches = SplitBatches(await ReadScriptAsync("01-tables.sql")).Append(ProductionColumns);
        if (withProcedures)
        {
            batches = batches.Concat(SplitBatches(await ReadScriptAsync("03-sprocs.sql"))).Append(LiveRoundSeasonSummary);
        }

        foreach (var batch in batches)
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }

        return connectionString;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private static Task<string> ReadScriptAsync(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "golftrack", name));

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
