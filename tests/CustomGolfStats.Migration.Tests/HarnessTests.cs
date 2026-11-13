using CustomGolfStats.Api.Data;
using CustomGolfStats.Migration.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CustomGolfStats.Migration.Tests;

public sealed class HarnessTests(MsSqlFixture sql, PostgresFixture postgres)
{
    [Fact]
    public async Task Golftrack_database_has_the_schema_the_migration_reads()
    {
        var connectionString = await sql.CreateDatabaseAsync();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            "SELECT TABLE_NAME + '.' + COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo'", connection);
        var columns = new HashSet<string>();
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                columns.Add(reader.GetString(0));
            }
        }

        string[] expected =
        [
            "Golfer.Id", "Course.Id", "Course.Latitude", "Course.Longitude", "Tee.CourseRating", "Hole.Handicap",
            "Round.Duration", "RoundDetail.SequenceNumber", "HandicapHistory.HandicapIndex", "RoundType.Name",
            "WeatherCondition.Name", "WindCondition.Name", "Temperature.Name",
        ];
        Assert.All(expected, column => Assert.Contains(column, columns));
    }

    [Fact]
    public void Script_is_split_on_go_lines_only()
    {
        var batches = MsSqlFixture.SplitBatches("SELECT 1\nGO\n  go  \nSELECT 'GOAL'\nGO").ToList();

        Assert.Equal(["SELECT 1\n", "\nSELECT 'GOAL'\n"], batches);
    }

    [Fact]
    public async Task Target_database_is_migrated()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseAppDatabase(connectionString).Options;
        await using var db = new AppDbContext(options, TimeProvider.System);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await db.Rounds.CountAsync(TestContext.Current.CancellationToken));
    }
}
