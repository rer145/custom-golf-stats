using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.App.Core.ViewModels;
using CustomGolfStats.Domain;

namespace CustomGolfStats.App.Core.Tests.ViewModels;

/// <summary>A number box the golfer cleared is a missing value, never the previous one.</summary>
public class BlankNumberTests
{
    [Fact]
    public async Task A_cleared_start_distance_blocks_the_shot_instead_of_recording_the_old_value()
    {
        await using var rig = await VmRig.CreateAsync();
        var roundId = await rig.CreateRoundAsync(EntryMode.ShotByShot);
        var vm = new ShotEntryViewModel(rig.Db, rig.Nav);
        await vm.LoadAsync(new HoleArgs(roundId, HoleNumber: 1));
        vm.EndLie = Lie.Fairway;
        vm.EndDistance = 100;

        vm.StartDistance = null; // the box was cleared
        await vm.AddShotCommand.ExecuteAsync(null);

        Assert.Empty(vm.Shots);
        Assert.Contains(UiText.StartDistanceMissing, vm.DraftErrors);
    }

    [Fact]
    public async Task A_cleared_hole_field_on_a_manual_course_is_reported_and_nothing_is_saved()
    {
        await using var rig = await VmRig.CreateAsync();
        var vm = new ManualCourseViewModel(rig.Db, rig.Temp.Clock, rig.Nav);
        vm.Initialize(9);
        vm.Name = "Blank Field GC";
        vm.Rating = 35;
        vm.Slope = 120;
        vm.Holes[2].Par = null;
        vm.Holes[3].Yardage = null;
        vm.Holes[4].StrokeIndex = null;

        var errors = vm.Validate();
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains(UiText.HoleParMissing(3), errors);
        Assert.Contains(UiText.HoleYardageMissing(4), errors);
        Assert.Contains(UiText.HoleStrokeIndexMissing(5), errors);
        Assert.Empty(rig.Nav.Visits);
        Assert.DoesNotContain(await rig.Db.Courses.SearchAsync(""), c => c.Name == "Blank Field GC");
    }
}
