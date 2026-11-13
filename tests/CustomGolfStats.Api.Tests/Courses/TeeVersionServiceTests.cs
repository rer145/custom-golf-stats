using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CustomGolfStats.Api.Tests.Courses;

public sealed class TeeVersionServiceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Jan2020 = new(2020, 1, 1);
    private static readonly DateOnly Jan2024 = new(2024, 1, 1);

    [Theory]
    [InlineData("2019-12-31", "2020-01-01")] // before every version: earliest
    [InlineData("2020-01-01", "2020-01-01")] // effective on the day
    [InlineData("2023-12-31", "2020-01-01")]
    [InlineData("2024-01-01", "2024-01-01")]
    [InlineData("2026-06-01", "2024-01-01")] // latest that is in effect
    public async Task Resolves_the_version_in_effect_on_the_date_played(string playedOn, string expectedEffectiveFrom)
    {
        var tee = await factory.WithDbAsync(async db =>
        {
            var seeded = await TestData.SeedCourseAsync(db, effectiveFrom: Jan2024);
            await TestData.AddTeeVersionAsync(db, seeded.Tee, Jan2020);
            return seeded.Tee;
        });

        var version = await ResolveAsync(tee.Id, DateOnly.Parse(playedOn));

        Assert.NotNull(version);
        Assert.Equal(DateOnly.Parse(expectedEffectiveFrom), version.EffectiveFrom);
        Assert.Equal(18, version.Holes.Count);
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_tee()
    {
        Assert.Null(await ResolveAsync(Guid.NewGuid(), Jan2024));
    }

    private async Task<Data.Entities.TeeVersion?> ResolveAsync(Guid teeId, DateOnly playedOn)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TeeVersionService>();
        return await service.ResolveAsync(teeId, playedOn, TestContext.Current.CancellationToken);
    }
}
