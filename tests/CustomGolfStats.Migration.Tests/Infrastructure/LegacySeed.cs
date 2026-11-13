using Microsoft.Data.SqlClient;

namespace CustomGolfStats.Migration.Tests.Infrastructure;

/// <summary>
/// Inserts synthetic golftrack rows into a test database (never real data). Every method returns the new row's id;
/// optional arguments fill the NOT NULL columns with plausible values.
/// </summary>
public sealed class LegacySeed(string connectionString)
{
    public Task<int> GolferAsync(string firstName = "Test", bool isActive = true) =>
        InsertAsync("Golfer", new()
        {
            ["FirstName"] = firstName,
            ["LastName"] = "Golfer",
            ["Email"] = $"{firstName.ToLowerInvariant()}@example.test",
            ["FacebookId"] = "",
            ["IsActive"] = isActive,
        });

    public Task<int> CourseAsync(
        string name = "Test Links",
        bool isActive = true,
        string address = "1 Fairway Dr",
        string? address2 = null,
        string city = "Springfield",
        string postalCode = "12345",
        string phone = "555-0100",
        string? website = null,
        decimal? latitude = null,
        decimal? longitude = null) =>
        InsertAsync("Course", new()
        {
            ["Name"] = name,
            ["Address"] = address,
            ["Address2"] = address2,
            ["City"] = city,
            ["StateId"] = 1,
            ["PostalCode"] = postalCode,
            ["CountryId"] = 1,
            ["PhoneNumber"] = phone,
            ["Website"] = website,
            ["IsActive"] = isActive,
            ["Latitude"] = latitude,
            ["Longitude"] = longitude,
        });

    public Task<int> TeeAsync(
        int courseId,
        string name = "White",
        string? gender = "M",
        int par = 72,
        double? rating = 70.1,
        int? slope = 125,
        int frontPar = 36,
        double? frontRating = 35.0,
        int? frontSlope = 124,
        int backPar = 36,
        double? backRating = 35.1,
        int? backSlope = 126,
        bool isActive = true) =>
        InsertAsync("Tee", new()
        {
            ["CourseId"] = courseId,
            ["Name"] = name,
            ["Gender"] = gender,
            ["Par"] = par,
            ["CourseRating"] = rating,
            ["SlopeRating"] = slope,
            ["FrontNinePar"] = frontPar,
            ["FrontNineCourseRating"] = frontRating,
            ["FrontNineSlopeRating"] = frontSlope,
            ["BackNinePar"] = backPar,
            ["BackNineCourseRating"] = backRating,
            ["BackNineSlopeRating"] = backSlope,
            ["IsActive"] = isActive,
        });

    public Task<int> HoleAsync(int teeId, int number, int par = 4, int yardage = 400, int? handicap = null, bool isActive = true) =>
        InsertAsync("Hole", new()
        {
            ["TeeId"] = teeId,
            ["Number"] = number,
            ["Par"] = par,
            ["Yardage"] = yardage,
            ["Handicap"] = handicap ?? number,
            ["IsActive"] = isActive,
        });

    /// <summary>Adds holes 1..<paramref name="count"/> (par 4, stroke index = number) and returns their ids in order.</summary>
    public async Task<IReadOnlyList<int>> HolesAsync(int teeId, int count = 18)
    {
        var ids = new List<int>();
        for (var number = 1; number <= count; number++)
        {
            ids.Add(await HoleAsync(teeId, number));
        }

        return ids;
    }

    public Task<int> LookupAsync(string table, string name, bool isActive = true) =>
        InsertAsync(table, new() { ["Name"] = name, ["IsActive"] = isActive });

    public Task<int> RoundAsync(
        int golferId,
        int teeId,
        DateTime playedOn,
        int score = 90,
        int? roundTypeId = null,
        int? weatherId = null,
        int? windId = null,
        int? temperatureId = null,
        int? holesPlayed = 18,
        string? duration = null,
        decimal? distance = null,
        string? notes = null,
        bool isActive = true) =>
        InsertAsync("Round", new()
        {
            ["GolferId"] = golferId,
            ["TeeId"] = teeId,
            ["RoundTypeId"] = roundTypeId,
            ["WeatherConditionId"] = weatherId,
            ["WindConditionId"] = windId,
            ["TemperatureId"] = temperatureId,
            ["HolesPlayed"] = holesPlayed,
            ["Score"] = score,
            ["Duration"] = duration,
            ["Distance"] = distance,
            ["PlayedOn"] = playedOn,
            ["Notes"] = notes,
            ["IsActive"] = isActive,
        });

    public Task<int> RoundDetailAsync(
        int roundId,
        int holeId,
        int? sequenceNumber = null,
        int score = 5,
        int putts = 2,
        bool fairwayHit = false,
        bool teeShotLeft = false,
        bool teeShotRight = false,
        bool teeShotLong = false,
        bool teeShotShort = false,
        bool gir = false,
        int penaltyShots = 0,
        int sandShots = 0,
        bool isActive = true) =>
        InsertAsync("RoundDetail", new()
        {
            ["RoundId"] = roundId,
            ["HoleId"] = holeId,
            ["SequenceNumber"] = sequenceNumber,
            ["Score"] = score,
            ["Putts"] = putts,
            ["FairwayHit"] = fairwayHit,
            ["TeeShotLeft"] = teeShotLeft,
            ["TeeShotRight"] = teeShotRight,
            ["TeeShotLong"] = teeShotLong,
            ["TeeShotShort"] = teeShotShort,
            ["GreenInRegulation"] = gir,
            ["PenaltyShots"] = penaltyShots,
            ["SandShots"] = sandShots,
            ["IsActive"] = isActive,
        });

    public Task<int> HandicapAsync(int golferId, DateTime date, decimal index) =>
        InsertAsync("HandicapHistory", new() { ["GolferId"] = golferId, ["HandicapDate"] = date, ["HandicapIndex"] = index });

    private async Task<int> InsertAsync(string table, Dictionary<string, object?> values)
    {
        var columns = values.Keys.ToList();
        var sql = $"INSERT INTO [dbo].[{table}] ({string.Join(", ", columns.Select(c => $"[{c}]"))}) " +
                  $"OUTPUT INSERTED.[Id] VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i}"))})";
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        for (var i = 0; i < columns.Count; i++)
        {
            command.Parameters.AddWithValue($"@p{i}", values[columns[i]] ?? DBNull.Value);
        }

        return (int)(await command.ExecuteScalarAsync())!;
    }
}
