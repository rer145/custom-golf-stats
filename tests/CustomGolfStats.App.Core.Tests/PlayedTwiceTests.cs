using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.App.Core.Tests.Support;
using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.App.Core.Tests;

/// <summary>The server identifies a hole result by (round, entry sequence), so a hole number may appear more than once.</summary>
public class PlayedTwiceTests
{
    private static RoundRecord RoundWithHoleOneTwice()
    {
        var round = Samples.Round();
        var first = HoleRecord.FromShots(round.Id, new HoleInfo(1, 4, 380, 7), 1, Samples.Par4Shots());
        var again = HoleRecord.FromShots(round.Id, new HoleInfo(1, 4, 380, 7), 2, Samples.Par5Shots());
        return new RoundRecord(round, [first, again]);
    }

    [Fact]
    public async Task The_same_hole_number_can_be_played_twice_with_each_playing_keeping_its_own_shots()
    {
        await using var t = await TempDb.CreateAsync();
        var round = RoundWithHoleOneTwice();
        await t.Db.Rounds.SaveAsync(round);

        var loaded = (await t.Db.Rounds.GetAsync(round.Round.Id))!;

        Assert.Equal([1, 1], loaded.Holes.Select(h => h.Hole.HoleNumber));
        Assert.Equal([1, 2], loaded.Holes.Select(h => h.Hole.EntrySequence));
        Assert.Equal(Samples.Par4Shots(), loaded.Holes[0].Shots.Select(s => s.ToShot()));
        Assert.Equal(Samples.Par5Shots(), loaded.Holes[1].Shots.Select(s => s.ToShot()));
        Assert.Equal(2, loaded.ToRoundData().Holes.Count);
    }

    [Fact]
    public async Task Replacing_or_deleting_one_playing_leaves_the_other_alone()
    {
        await using var t = await TempDb.CreateAsync();
        var round = RoundWithHoleOneTwice();
        await t.Db.Rounds.SaveAsync(round);

        await t.Db.Rounds.SaveHoleAsync(round.Round.Id, HoleRecord.FromShots(round.Round.Id, new HoleInfo(1, 4, 380, 7), 2, Samples.Par4Shots()));
        var afterReplace = (await t.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.Equal(Samples.Par4Shots(), afterReplace.Holes[0].Shots.Select(s => s.ToShot()));
        Assert.Equal(Samples.Par4Shots(), afterReplace.Holes[1].Shots.Select(s => s.ToShot()));

        await t.Db.Rounds.DeleteHoleAsync(round.Round.Id, 1);
        var afterDelete = (await t.Db.Rounds.GetAsync(round.Round.Id))!;
        Assert.Equal([2], afterDelete.Holes.Select(h => h.Hole.EntrySequence));
        Assert.Equal(4, await t.Db.Connection.Table<ShotRow>().CountAsync());
    }

    [Fact]
    public void Api_mapping_keeps_both_playings_and_their_shots()
    {
        var round = RoundWithHoleOneTwice();

        var back = ApiMapper.ToRecord(new CustomGolfStats.Api.Client.RoundDto
        {
            Id = round.Round.Id,
            Holes = [.. ApiMapper.ToUpsertRequest(round).Holes!.Select(h => new CustomGolfStats.Api.Client.HoleResultDto { HoleNumber = h.HoleNumber, EntrySequence = h.EntrySequence, Shots = h.Shots! })],
        });

        Assert.Equal([1, 2], back.Holes.Select(h => h.Hole.EntrySequence));
        Assert.Equal(4, back.Holes[0].Shots.Count);
        Assert.Equal(Samples.Par5Shots(), back.Holes[1].Shots.Select(s => s.ToShot()));
    }
}
