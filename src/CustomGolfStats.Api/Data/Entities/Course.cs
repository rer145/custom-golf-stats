namespace CustomGolfStats.Api.Data.Entities;

/// <summary>Shared by everyone. <see cref="ProviderCourseId"/> is unique per <see cref="Source"/> when set.</summary>
public class Course
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

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

    public string? ProviderCourseId { get; set; }

    public DateTimeOffset? LastVerifiedAt { get; set; }

    /// <summary>Set when an admin merged this course into another; merged courses are hidden from search.</summary>
    public Guid? MergedIntoCourseId { get; set; }

    public Course? MergedIntoCourse { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<Tee> Tees { get; set; } = [];
}
