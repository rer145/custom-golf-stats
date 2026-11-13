using CommunityToolkit.Mvvm.ComponentModel;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.App.Core.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>A sentence for the golfer, or null when all is well.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Runs a command's work so that no exception reaches the UI: any failure becomes <see cref="ErrorMessage"/> and false is
    /// returned. An <see cref="InvalidOperationException"/> (the round repository's "no such live round", for example a round deleted on
    /// another device and pulled as a tombstone) shows <paramref name="invalidOperationMessage"/>. Cancellation still propagates.
    /// The app must also install a global unhandled-exception handler as a last resort.</summary>
    protected async Task<bool> TryAsync(Func<Task> action, string? invalidOperationMessage = null)
    {
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException) when (invalidOperationMessage is not null)
        {
            ErrorMessage = invalidOperationMessage;
        }
        catch (Exception)
        {
            ErrorMessage = UiText.SomethingWentWrong;
        }
        return false;
    }
}

/// <summary>A round plus the hole layout of its tee version, loaded together by the round-level view models.</summary>
internal sealed record RoundContext(RoundRecord Record, IReadOnlyList<HoleInfo> Layout, TeeVersionRow? Version)
{
    public static async Task<RoundContext?> LoadAsync(LocalDb db, Guid roundId)
    {
        var record = await db.Rounds.GetAsync(roundId);
        if (record is null)
            return null;
        // The server may have given the tee different version ids than the ones created on this device.
        var version = await db.Courses.ResolveVersionAsync(record.Round.TeeVersionId, record.Round.TeeId, record.Round.PlayedOn);
        var layout = version is null ? [] : await db.Courses.GetHoleInfosAsync(version.Id);
        return new RoundContext(record, layout, version);
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
