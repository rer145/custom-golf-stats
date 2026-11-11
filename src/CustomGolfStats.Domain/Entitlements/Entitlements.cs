// Namespace deliberately omits the folder name: a CustomGolfStats.Domain.Entitlements namespace would shadow this class.
#pragma warning disable IDE0130
namespace CustomGolfStats.Domain;
#pragma warning restore IDE0130

public static class Entitlements
{
    public const int DefaultTrialShotRounds = 3;

    /// <summary>Paid users have every feature. Free users get shot-by-shot entry only while trial rounds remain.</summary>
    public static bool CanUse(Feature feature, Tier tier, int trialRoundsUsed, int trialAllowance)
    {
        ThrowIfNegative(trialRoundsUsed, trialAllowance);
        return tier == Tier.Paid || TrialRoundsRemaining(tier, trialRoundsUsed, trialAllowance) > 0;
    }

    /// <summary>Null means unlimited (Paid). Never negative.</summary>
    public static int? TrialRoundsRemaining(Tier tier, int trialRoundsUsed, int trialAllowance)
    {
        ThrowIfNegative(trialRoundsUsed, trialAllowance);
        if (tier == Tier.Paid)
        {
            return null;
        }

        return Math.Max(0, trialAllowance - trialRoundsUsed);
    }

    private static void ThrowIfNegative(int trialRoundsUsed, int trialAllowance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trialRoundsUsed);
        ArgumentOutOfRangeException.ThrowIfNegative(trialAllowance);
    }
}
