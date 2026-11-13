using System.Collections.Concurrent;
using CustomGolfStats.Api.Features.Courses;

namespace CustomGolfStats.Api.Tests.Infrastructure.Fakes;

/// <summary>In-memory GolfCourseAPI. Add courses with <see cref="Add"/>; calls are counted for assertions.</summary>
public sealed class FakeCourseProvider : ICourseProvider
{
    private readonly ConcurrentDictionary<string, ProviderCourse> _courses = new();
    private int _searchCalls;
    private int _getCalls;

    public int SearchCalls => _searchCalls;

    public int GetCalls => _getCalls;

    public void Add(ProviderCourse course) => _courses[course.ProviderCourseId] = course;

    public Task<IReadOnlyList<ProviderCourseSummary>> SearchAsync(string query, CancellationToken ct)
    {
        Interlocked.Increment(ref _searchCalls);
        IReadOnlyList<ProviderCourseSummary> results = _courses.Values
            .Where(c => c.ClubName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.CourseName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.ProviderCourseId, StringComparer.Ordinal)
            .Select(c => new ProviderCourseSummary(c.ProviderCourseId, c.ClubName, c.CourseName, c.City, c.Region, c.Country))
            .ToList();
        return Task.FromResult(results);
    }

    public Task<ProviderCourse?> GetAsync(string providerCourseId, CancellationToken ct)
    {
        Interlocked.Increment(ref _getCalls);
        return Task.FromResult(_courses.GetValueOrDefault(providerCourseId));
    }
}
