namespace CustomGolfStats.Api.Data.Entities;

/// <summary>
/// A tee's rating, slope and holes from <see cref="EffectiveFrom"/> on. Unique per (tee, effective from).
/// A round points at the version in effect on the date it was played.
/// </summary>
public class TeeVersion
{
    public Guid Id { get; set; }

    public Guid TeeId { get; set; }

    public Tee Tee { get; set; } = null!;

    public DateOnly EffectiveFrom { get; set; }

    public int Par { get; set; }

    /// <summary>Course rating, numeric(4,1).</summary>
    public decimal Rating { get; set; }

    public int Slope { get; set; }

    public int? FrontPar { get; set; }

    public decimal? FrontRating { get; set; }

    public int? FrontSlope { get; set; }

    public int? BackPar { get; set; }

    public decimal? BackRating { get; set; }

    public int? BackSlope { get; set; }

    public CourseSource Source { get; set; }

    public Guid? ChangedByAccountId { get; set; }

    public Account? ChangedByAccount { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public List<TeeHole> Holes { get; set; } = [];
}
