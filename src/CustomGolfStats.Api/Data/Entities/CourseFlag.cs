namespace CustomGolfStats.Api.Data.Entities;

/// <summary>A golfer's report that course data is wrong (or a proposed edit to a verified course).</summary>
public class CourseFlag
{
    public Guid Id { get; set; }

    public Guid CourseId { get; set; }

    public Course Course { get; set; } = null!;

    public Guid? TeeId { get; set; }

    public Tee? Tee { get; set; }

    public int? HoleNumber { get; set; }

    public Guid ReporterAccountId { get; set; }

    public Account Reporter { get; set; } = null!;

    public required string Note { get; set; }

    public FlagStatus Status { get; set; } = FlagStatus.Open;

    public Guid? ResolvedByAccountId { get; set; }

    public Account? ResolvedBy { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
