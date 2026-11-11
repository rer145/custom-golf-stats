namespace CustomGolfStats.Domain.Tests.Access;

public class EntitlementsTests
{
    [Fact]
    public void Paid_can_always_use_shot_entry()
    {
        Assert.True(Entitlements.CanUse(Feature.ShotByShotEntry, Tier.Paid, 50, 3));
        Assert.Null(Entitlements.TrialRoundsRemaining(Tier.Paid, 50, 3));
    }

    [Fact]
    public void Free_with_trial_left_can_use()
    {
        Assert.True(Entitlements.CanUse(Feature.ShotByShotEntry, Tier.Free, 2, 3));
        Assert.Equal(1, Entitlements.TrialRoundsRemaining(Tier.Free, 2, 3));
    }

    [Fact]
    public void Free_with_trial_used_up_cannot_use()
    {
        Assert.False(Entitlements.CanUse(Feature.ShotByShotEntry, Tier.Free, 3, 3));
        Assert.Equal(0, Entitlements.TrialRoundsRemaining(Tier.Free, 3, 3));
    }

    [Fact]
    public void Over_used_trial_reports_zero_not_negative()
    {
        Assert.Equal(0, Entitlements.TrialRoundsRemaining(Tier.Free, 5, 3));
    }

    [Fact]
    public void Zero_allowance_blocks_free()
    {
        Assert.False(Entitlements.CanUse(Feature.ShotByShotEntry, Tier.Free, 0, 0));
    }

    [Fact]
    public void Negative_inputs_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Entitlements.CanUse(Feature.ShotByShotEntry, Tier.Free, -1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => Entitlements.CanUse(Feature.ShotByShotEntry, Tier.Free, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Entitlements.TrialRoundsRemaining(Tier.Paid, -1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => Entitlements.TrialRoundsRemaining(Tier.Free, 0, -1));
    }

    [Fact]
    public void Default_trial_allowance_is_three()
    {
        Assert.Equal(3, Entitlements.DefaultTrialShotRounds);
    }
}
