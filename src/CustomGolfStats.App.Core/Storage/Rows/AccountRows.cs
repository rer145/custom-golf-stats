using CustomGolfStats.Domain;
using SQLite;

namespace CustomGolfStats.App.Core.Storage.Rows;

[Table("HandicapRow")]
public sealed class HandicapRow
{
    [PrimaryKey] public Guid Id { get; set; }
    [Indexed] public DateTime EffectiveOn { get; set; }
    public double Index { get; set; }
}

/// <summary>The cached entitlements (always a single row, <see cref="SingletonId"/>). The server is the source of truth;
/// offline, <see cref="TrialShotRoundsRemaining"/> is decremented locally and replaced on the next sync.</summary>
[Table("EntitlementsRow")]
public sealed class EntitlementsRow
{
    public const int SingletonId = 1;

    [PrimaryKey] public int Id { get; set; } = SingletonId;
    public Tier Tier { get; set; }
    public Role Role { get; set; }
    public bool ShotByShotEntry { get; set; }
    /// <summary>Null means unlimited (paid).</summary>
    public int? TrialShotRoundsRemaining { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

[Table("SyncStateRow")]
public sealed class SyncStateRow
{
    [PrimaryKey] public string Key { get; set; } = "";
    public string? Value { get; set; }
}
