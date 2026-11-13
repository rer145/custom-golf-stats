namespace CustomGolfStats.Api.Data.Entities;

public class Tee
{
    public Guid Id { get; set; }

    public Guid CourseId { get; set; }

    public Course Course { get; set; } = null!;

    public required string Name { get; set; }

    public Gender Gender { get; set; }

    public List<TeeVersion> Versions { get; set; } = [];
}
