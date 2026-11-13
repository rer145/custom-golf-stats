using CustomGolfStats.Api.Client;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Tests.Support;

namespace CustomGolfStats.App.Core.Tests;

public class CourseMappingParityTests
{
    [Fact]
    public void A_course_detail_and_a_sync_course_with_the_same_content_map_to_the_same_record()
    {
        var holes = new[]
        {
            new TeeHoleDto { Number = 1, Par = 4, Yardage = 400, StrokeIndex = 1 },
            new TeeHoleDto { Number = 2, Par = 3, Yardage = 150, StrokeIndex = 2 },
        };
        var effective = new DateTimeOffset(2021, 3, 4, 0, 0, 0, TimeSpan.FromHours(10));
        var sync = new SyncCourseDto
        {
            Id = Samples.CourseId, Name = "X", City = "C", Latitude = 1.5, Longitude = 2.5, Status = CourseStatus.Verified, Source = CourseSource.Manual,
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Tees = [new SyncTeeDto { Id = Samples.TeeId, Name = "Blue", Gender = Gender.Female, Versions = [new SyncTeeVersionDto { Id = Samples.TeeVersionId, EffectiveFrom = effective, Par = 7, Rating = 70.1, Slope = 120, FrontPar = 3, BackRating = 35.5, Holes = holes }] }],
        };
        var detail = new CourseDetailDto
        {
            Id = Samples.CourseId, Name = "X", City = "C", Latitude = 1.5, Longitude = 2.5, Status = CourseStatus.Verified, Source = CourseSource.Manual,
            Tees = [new TeeDto { Id = Samples.TeeId, Name = "Blue", Gender = Gender.Female, Versions = [new TeeVersionDto { Id = Samples.TeeVersionId, EffectiveFrom = effective, Par = 7, Rating = 70.1, Slope = 120, FrontPar = 3, BackRating = 35.5, Holes = holes }] }],
        };
        var received = new DateTimeOffset(2026, 7, 7, 7, 0, 0, TimeSpan.Zero);

        var fromSync = ApiMapper.ToRecord(sync);
        var fromDetail = ApiMapper.ToRecord(detail, received);

        Assert.Equal(sync.UpdatedAt, fromSync.Course.UpdatedAt);
        Assert.Equal(received, fromDetail.Course.UpdatedAt); // the caller's clock, not the machine's
        fromDetail.Course.UpdatedAt = fromSync.Course.UpdatedAt;
        Assert.Equivalent(fromSync, fromDetail, strict: true);
        Assert.Equal(new DateTime(2021, 3, 4), fromDetail.Tees[0].Versions[0].Version.EffectiveFrom);
    }
}
