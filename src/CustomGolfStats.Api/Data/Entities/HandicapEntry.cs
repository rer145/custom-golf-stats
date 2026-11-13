namespace CustomGolfStats.Api.Data.Entities;

public class HandicapEntry
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    public DateOnly EffectiveOn { get; set; }

    /// <summary>Handicap index, numeric(4,1).</summary>
    public decimal Index { get; set; }
}
