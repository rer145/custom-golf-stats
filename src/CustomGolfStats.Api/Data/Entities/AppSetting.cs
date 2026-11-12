namespace CustomGolfStats.Api.Data.Entities;

/// <summary>A key/value setting an admin can change. Known keys are in <see cref="AppSettingKeys"/>.</summary>
public class AppSetting
{
    public required string Key { get; set; }

    public required string Value { get; set; }
}

public static class AppSettingKeys
{
    /// <summary>How many shot-by-shot rounds a Free golfer may log (integer, seeded to 3).</summary>
    public const string TrialShotRounds = "TrialShotRounds";
}
