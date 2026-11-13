using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Repositories;
using CustomGolfStats.App.Core.Tests.Support;

namespace CustomGolfStats.App.Core.Tests;

public class TombstonePushTests
{
    [Fact]
    public async Task Push_accepted_never_writes_over_a_tombstone_even_when_the_clock_instant_matches()
    {
        await using var t = await TempDb.CreateAsync();
        var round = Samples.FullShotRound();
        await t.Db.Rounds.SaveAsync(round);
        var pushed = t.Clock.UtcNow;
        await t.Db.Rounds.MarkDeletedAsync(round.Round.Id); // same fake-clock instant: ClientUpdatedAt == pushed

        var copy = new RoundRecord(round.Round.Clone(), round.Holes);
        var stored = await t.Db.Rounds.SaveFromServerAsync(copy, ServerWriteMode.PushAccepted(pushed));

        Assert.False(stored);
        Assert.Null(await t.Db.Rounds.GetAsync(round.Round.Id));
        Assert.True((await t.Db.Rounds.GetAsync(round.Round.Id, includeDeleted: true))!.Round.Dirty);
    }
}
