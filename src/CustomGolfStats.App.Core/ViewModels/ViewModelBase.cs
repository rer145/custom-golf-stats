using CommunityToolkit.Mvvm.ComponentModel;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.App.Core.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>A sentence for the golfer, or null when all is well.</summary>
    [ObservableProperty]
    private string? _errorMessage;
}

/// <summary>A round plus the hole layout of its tee version, loaded together by the round-level view models.</summary>
internal sealed record RoundContext(RoundRecord Record, IReadOnlyList<HoleInfo> Layout)
{
    public static async Task<RoundContext?> LoadAsync(LocalDb db, Guid roundId)
    {
        var record = await db.Rounds.GetAsync(roundId);
        if (record is null)
            return null;
        var layout = await db.Courses.GetHoleInfosAsync(record.Round.TeeVersionId);
        return new RoundContext(record, layout);
    }

    public int NextEntrySequence => Record.Holes.Count == 0 ? 1 : Record.Holes.Max(h => h.Hole.EntrySequence) + 1;

    /// <summary>The hole after the last one entered (wrapping), or the first hole of the layout for a new round.</summary>
    public int? SuggestNextHole()
    {
        if (Layout.Count == 0)
            return null;
        var last = Record.Holes.OrderByDescending(h => h.Hole.EntrySequence).FirstOrDefault();
        if (last is null)
            return Layout[0].Number;
        var index = Layout.ToList().FindIndex(h => h.Number == last.Hole.HoleNumber);
        return index < 0 ? Layout[0].Number : Layout[(index + 1) % Layout.Count].Number;
    }
}
