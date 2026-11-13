using System.Data;
using Microsoft.Data.SqlClient;

namespace CustomGolfStats.Migration.Parity;

/// <summary>One result set: rows of column name (case-insensitive) to value, or the error that stopped the procedure.</summary>
public sealed record LegacyResult(string Procedure, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, string? Error = null);

/// <summary>Per-year facts about the golfer's golftrack data, read with SELECTs.</summary>
public sealed record LegacyYearFacts(
    int Year, int GolferRounds, int OtherGolfersRounds, int InactiveDetails, int StoredScoreDiffers, int HolesPlayedDiffers);

/// <summary>
/// Runs golftrack's read-only statistics procedures (only those named in <see cref="AllowedProcedures"/>; anything else
/// is refused) and plain SELECTs. A procedure that fails (missing in the live database, divide by zero on empty data)
/// gives a <see cref="LegacyResult"/> with its error instead of throwing.
/// </summary>
public sealed class LegacyStatsReader(string connectionString)
{
    public static readonly IReadOnlySet<string> AllowedProcedures = new HashSet<string>(StringComparer.Ordinal)
    {
        "Stats_ScoringStats", "Stats_DrivingStats", "Stats_ApproachStats", "Stats_PuttingStats", "Round_SeasonSummary",
        "Stats_CareerStats", "Stats_CourseStats",
    };

    public async Task<LegacyResult> RunAsync(
        string procedure, IReadOnlyDictionary<string, int> parameters, CancellationToken cancellationToken = default)
    {
        if (!AllowedProcedures.Contains(procedure))
        {
            throw new InvalidOperationException($"{procedure} is not one of golftrack's read-only statistics procedures.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"dbo.[{procedure}]", connection) { CommandType = CommandType.StoredProcedure };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new SqlParameter("@" + name, SqlDbType.Int) { Value = value });
        }

        try
        {
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                rows.Add(row);
            }

            return new LegacyResult(procedure, rows);
        }
        catch (SqlException e)
        {
            return new LegacyResult(procedure, [], e.Message);
        }
    }

    public async Task<IReadOnlyList<LegacyYearFacts>> ReadYearFactsAsync(int golferId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH Mine AS (
                SELECT r.Id, r.Score, r.HolesPlayed, YEAR(r.PlayedOn) AS [Year],
                       (SELECT COUNT(*) FROM dbo.RoundDetail d WHERE d.RoundId = r.Id AND d.IsActive = 1) AS Details,
                       (SELECT ISNULL(SUM(d.Score), 0) FROM dbo.RoundDetail d WHERE d.RoundId = r.Id AND d.IsActive = 1) AS DetailScore,
                       (SELECT COUNT(*) FROM dbo.RoundDetail d WHERE d.RoundId = r.Id AND d.IsActive = 0) AS InactiveDetails
                FROM dbo.Round r
                WHERE r.GolferId = @golferId AND r.IsActive = 1)
            SELECT m.[Year],
                   COUNT(*),
                   (SELECT COUNT(*) FROM dbo.Round o WHERE o.GolferId <> @golferId AND o.IsActive = 1 AND YEAR(o.PlayedOn) = m.[Year]),
                   SUM(m.InactiveDetails),
                   SUM(CASE WHEN m.Score <> m.DetailScore THEN 1 ELSE 0 END),
                   SUM(CASE WHEN m.HolesPlayed IS NULL OR m.HolesPlayed <> m.Details THEN 1 ELSE 0 END)
            FROM Mine m
            GROUP BY m.[Year]
            ORDER BY m.[Year]
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@golferId", SqlDbType.Int) { Value = golferId });
        var facts = new List<LegacyYearFacts>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            facts.Add(new LegacyYearFacts(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)));
        }

        return facts;
    }
}
