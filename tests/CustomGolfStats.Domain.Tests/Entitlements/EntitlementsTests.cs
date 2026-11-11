using static CustomGolfStats.Domain.Entitlements.Entitlements;

namespace CustomGolfStats.Domain.Tests;

public class EntitlementsTests
{
    [Fact]
    public void Paid_can_always_use_shot_entry()
    {
        Assert.True(CanUse(Feature.ShotByShotEntry, Tier.Paid, 50, 3));
        Assert.Null(TrialRoundsRemaining(Tier.Paid, 50, 3));
    }

    [Fact]
    public void Free_with_trial_left_can_use()
    {
        Assert.True(CanUse(Feature.ShotByShotEntry, Tier.Free, 2, 3));
        Assert.Equal(1, TrialRoundsRemaining(Tier.Free, 2, 3));
    }

    [Fact]
    public void Free_with_trial_used_up_cannot_use()
    {
        Assert.False(CanUse(Feature.ShotByShotEntry, Tier.Free, 3, 3));
        Assert.Equal(0, TrialRoundsRemaining(Tier.Free, 3, 3));
    }

    [Fact]
    public void Over_used_trial_reports_zero_not_negative()
    {
        Assert.Equal(0, TrialRoundsRemaining(Tier.Free, 5, 3));
    }

    [Fact]
    public void Zero_allowance_blocks_free()
    {
        Assert.False(CanUse(Feature.ShotByShotEntry, Tier.Free, 0, 0));
    }

    [Fact]
    public void Default_trial_allowance_is_three()
    {
        Assert.Equal(3, DefaultTrialShotRounds);
    }
}
