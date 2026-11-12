namespace CustomGolfStats.Api.Data.Entities;

/// <summary>One hole of a tee version. Unique per (tee version, number).</summary>
public class TeeHole
{
    public Guid Id { get; set; }

    public Guid TeeVersionId { get; set; }

    public TeeVersion TeeVersion { get; set; } = null!;

    public int Number { get; set; }

    public int Par { get; set; }

    public int Yardage { get; set; }

    public int StrokeIndex { get; set; }
}
