using Microsoft.Data.SqlClient;

namespace CustomGolfStats.Migration.Source;

/// <summary>
/// Reads one golfer's data from golftrack with plain, parameterised <c>SELECT</c> statements (nothing else is ever run).
/// Rounds and round details are read only when <c>IsActive = 1</c>. Every course, tee and hole those rounds play is read
/// whatever its own <c>IsActive</c>: golftrack's Stats_* procedures ignore it there, and many played tees were
/// deactivated later. Tees no active round plays are not read. Lookup tables are read whole (names only).
/// </summary>
public sealed class GolftrackReader(string connectionString)
{
    private const string RoundTees = "SELECT r.TeeId FROM dbo.Round r WHERE r.GolferId = @golferId AND r.IsActive = 1";

    public async Task<LegacyData> ReadAsync(int golferId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var courses = await QueryAsync(
            connection,
            golferId,
            $"""
            SELECT c.Id, c.Name, c.Address, c.Address2, c.City, c.PostalCode, c.PhoneNumber, c.Website, c.Latitude, c.Longitude
            FROM dbo.Course c
            WHERE c.Id IN (SELECT t.CourseId FROM dbo.Tee t WHERE t.Id IN ({RoundTees}))
            ORDER BY c.Id
            """,
            r => new LegacyCourse(
                r.GetInt32(0), r.GetString(1), NullableString(r, 2), NullableString(r, 3), NullableString(r, 4),
                NullableString(r, 5), NullableString(r, 6), NullableString(r, 7), NullableDecimal(r, 8), NullableDecimal(r, 9)),
            cancellationToken);

        var tees = await QueryAsync(
            connection,
            golferId,
            $"""
            SELECT t.Id, t.CourseId, t.Name, t.Gender, t.Par, t.CourseRating, t.SlopeRating,
                   t.FrontNinePar, t.FrontNineCourseRating, t.FrontNineSlopeRating,
                   t.BackNinePar, t.BackNineCourseRating, t.BackNineSlopeRating
            FROM dbo.Tee t
            WHERE t.Id IN ({RoundTees})
            ORDER BY t.Id
            """,
            r => new LegacyTee(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), NullableString(r, 3), r.GetInt32(4), NullableDouble(r, 5),
                NullableInt(r, 6), r.GetInt32(7), NullableDouble(r, 8), NullableInt(r, 9), r.GetInt32(10),
                NullableDouble(r, 11), NullableInt(r, 12)),
            cancellationToken);

        var holes = await QueryAsync(
            connection,
            golferId,
            $"""
            SELECT h.Id, h.TeeId, h.Number, h.Par, h.Yardage, h.Handicap, h.IsActive
            FROM dbo.Hole h
            WHERE h.TeeId IN ({RoundTees})
            ORDER BY h.Id
            """,
            r => new LegacyHole(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetBoolean(6)),
            cancellationToken);

        var rounds = await QueryAsync(
            connection,
            golferId,
            """
            SELECT r.Id, r.GolferId, r.TeeId, r.RoundTypeId, r.WeatherConditionId, r.WindConditionId, r.TemperatureId,
                   r.HolesPlayed, r.Score, r.Duration, r.Distance, r.PlayedOn, r.Notes
            FROM dbo.Round r
            WHERE r.GolferId = @golferId AND r.IsActive = 1
            ORDER BY r.Id
            """,
            r => new LegacyRound(
                r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), NullableInt(r, 3), NullableInt(r, 4), NullableInt(r, 5),
                NullableInt(r, 6), NullableInt(r, 7), r.GetInt32(8), NullableString(r, 9), NullableDecimal(r, 10),
                r.GetDateTime(11), NullableString(r, 12)),
            cancellationToken);

        var details = await QueryAsync(
            connection,
            golferId,
            """
            SELECT d.Id, d.RoundId, d.HoleId, d.SequenceNumber, d.Score, d.Putts, d.FairwayHit, d.TeeShotLeft,
                   d.TeeShotRight, d.TeeShotLong, d.TeeShotShort, d.GreenInRegulation, d.PenaltyShots, d.SandShots
            FROM dbo.RoundDetail d
            JOIN dbo.Round r ON r.Id = d.RoundId
            WHERE r.GolferId = @golferId AND r.IsActive = 1 AND d.IsActive = 1
            ORDER BY d.Id
            """,
            r => new LegacyRoundDetail(
                r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), NullableInt(r, 3), r.GetInt32(4), r.GetInt32(5),
                r.GetBoolean(6), r.GetBoolean(7), r.GetBoolean(8), r.GetBoolean(9), r.GetBoolean(10), r.GetBoolean(11),
                r.GetInt32(12), r.GetInt32(13)),
            cancellationToken);

        var handicaps = await QueryAsync(
            connection,
            golferId,
            """
            SELECT h.Id, h.GolferId, h.HandicapDate, h.HandicapIndex
            FROM dbo.HandicapHistory h
            WHERE h.GolferId = @golferId
            ORDER BY h.Id
            """,
            r => new LegacyHandicap(r.GetInt32(0), r.GetInt32(1), r.GetDateTime(2), r.GetDecimal(3)),
            cancellationToken);

        return new LegacyData(
            golferId,
            courses,
            tees,
            holes,
            rounds,
            details,
            handicaps,
            await LookupAsync(connection, "RoundType", cancellationToken),
            await LookupAsync(connection, "WeatherCondition", cancellationToken),
            await LookupAsync(connection, "WindCondition", cancellationToken),
            await LookupAsync(connection, "Temperature", cancellationToken));
    }

    private static async Task<IReadOnlyDictionary<int, string>> LookupAsync(
        SqlConnection connection, string table, CancellationToken cancellationToken)
    {
        // The table name is one of four constants above, never input.
        var rows = await QueryAsync(
            connection, null, $"SELECT Id, Name FROM dbo.[{table}] ORDER BY Id", r => (Id: r.GetInt32(0), Name: r.GetString(1)),
            cancellationToken);
        return rows.ToDictionary(row => row.Id, row => row.Name);
    }

    private static async Task<IReadOnlyList<T>> QueryAsync<T>(
        SqlConnection connection, int? golferId, string sql, Func<SqlDataReader, T> map, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        if (golferId is { } id)
        {
            command.Parameters.Add(new SqlParameter("@golferId", System.Data.SqlDbType.Int) { Value = id });
        }

        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private static string? NullableString(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static int? NullableInt(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    private static double? NullableDouble(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private static decimal? NullableDecimal(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDecimal(i);
}
