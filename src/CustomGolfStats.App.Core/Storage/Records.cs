using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Shots;
using RoundData = CustomGolfStats.Domain.Stats.RoundData;

namespace CustomGolfStats.App.Core.Storage;

/// <summary>One hole with its shots (empty for quick entry), in sequence order.</summary>
public sealed record HoleRecord(HoleRow Hole, IReadOnlyList<ShotRow> Shots)
{
    public HoleSummary ToSummary() => Hole.ToSummary();

    /// <summary>A shot-entered hole: the summary comes from <see cref="HoleSummaryDeriver.Derive"/>, never from local logic.</summary>
    public static HoleRecord FromShots(Guid roundId, HoleInfo info, int entrySequence, IReadOnlyList<Shot> shots)
    {
        var summary = HoleSummaryDeriver.Derive(info, entrySequence, shots);
        var rows = shots.OrderBy(s => s.Sequence).Select(s => ShotRow.FromShot(roundId, info.Number, s)).ToList();
        return new HoleRecord(HoleRow.FromSummary(roundId, summary, isDerived: true), rows);
    }
}

/// <summary>A round with its holes (ordered by hole number) and their shots.</summary>
public sealed record RoundRecord(RoundRow Round, IReadOnlyList<HoleRecord> Holes)
{
    public RoundData ToRoundData() => new(Round.Id, DateOnly.FromDateTime(Round.PlayedOn), Round.CourseId, Round.CourseName,
        Round.TeeId, Round.TeeName, Holes.OrderBy(h => h.Hole.EntrySequence).Select(h => h.ToSummary()).ToList());
}

public sealed record TeeVersionRecord(TeeVersionRow Version, List<TeeHoleRow> Holes);

public sealed record TeeRecord(TeeRow Tee, List<TeeVersionRecord> Versions);

public sealed record CourseRecord(CourseRow Course, List<TeeRecord> Tees);
