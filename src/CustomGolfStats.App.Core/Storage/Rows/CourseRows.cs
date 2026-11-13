using CustomGolfStats.Domain.Holes;
using SQLite;
using CourseSource = CustomGolfStats.Api.Client.CourseSource;
using CourseStatus = CustomGolfStats.Api.Client.CourseStatus;
using Gender = CustomGolfStats.Api.Client.Gender;

namespace CustomGolfStats.App.Core.Storage.Rows;

/// <summary>A cached or locally created course. <see cref="PendingSync"/> marks a manual course not yet pushed.</summary>
[Table("CourseRow")]
public sealed class CourseRow
{
    [PrimaryKey] public Guid Id { get; set; }
    [Indexed] public string Name { get; set; } = "";
    public string? ClubName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public CourseStatus Status { get; set; }
    public CourseSource Source { get; set; }
    public Guid? MergedIntoCourseId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsFavorite { get; set; }
    [Indexed] public bool PendingSync { get; set; }
}

[Table("TeeRow")]
public sealed class TeeRow
{
    [PrimaryKey] public Guid Id { get; set; }
    [Indexed] public Guid CourseId { get; set; }
    public string Name { get; set; } = "";
    public Gender Gender { get; set; }
}

[Table("TeeVersionRow")]
public sealed class TeeVersionRow
{
    [PrimaryKey] public Guid Id { get; set; }
    [Indexed] public Guid TeeId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public int Par { get; set; }
    public double Rating { get; set; }
    public int Slope { get; set; }
    public int? FrontPar { get; set; }
    public double? FrontRating { get; set; }
    public int? FrontSlope { get; set; }
    public int? BackPar { get; set; }
    public double? BackRating { get; set; }
    public int? BackSlope { get; set; }
}

[Table("TeeHoleRow")]
public sealed class TeeHoleRow
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed("IX_TeeHoleRow_Version_Number", 0, Unique = true)] public Guid TeeVersionId { get; set; }
    [Indexed("IX_TeeHoleRow_Version_Number", 1, Unique = true)] public int Number { get; set; }
    public int Par { get; set; }
    public int Yardage { get; set; }
    public int StrokeIndex { get; set; }

    public HoleInfo ToHoleInfo() => new(Number, Par, Yardage, StrokeIndex);
}
