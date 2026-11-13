using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;
using CustomGolfStats.Domain.Shots;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>Yards everywhere except feet on the green.</summary>
public static class DistanceUnits
{
    public static string For(Lie lie) => lie == Lie.Green ? UiText.UnitFeet : UiText.UnitYards;

    /// <summary>The same physical distance in the unit of the new lie: yards to feet (x3) or feet to yards (rounded) when the lie
    /// moves on or off the green; unchanged otherwise.</summary>
    public static int Convert(int distance, Lie from, Lie to)
    {
        var fromFeet = from == Lie.Green;
        var toFeet = to == Lie.Green;
        if (fromFeet == toFeet)
            return distance;
        return toFeet ? distance * 3 : (int)Math.Round(distance / 3.0, MidpointRounding.AwayFromZero);
    }
}

/// <summary>One recorded shot with the unit its distances are in.</summary>
public sealed record ShotItem(Shot Shot)
{
    public string StartUnit => DistanceUnits.For(Shot.StartLie);
    public string? EndUnit => Shot.EndLie == Lie.Holed ? null : DistanceUnits.For(Shot.EndLie);
}

/// <summary>
/// Shot-by-shot entry of one hole. The start lie and distance of each new shot are prefilled from where the previous one ended
/// (out of bounds replays from the same spot, a penalty area drops in the rough at the distance it crossed), putts are in feet
/// on the green and everything else in yards, penalty strokes are zero or more with no limit, and the summary shown live is
/// <see cref="HoleSummaryDeriver.Derive"/>. Validation is <see cref="ShotValidator"/>.
/// </summary>
public sealed partial class ShotEntryViewModel(LocalDb db, INavigator navigator) : ViewModelBase
{
    private static readonly Lie[] StartLieChoices = [Lie.Tee, Lie.Fairway, Lie.Rough, Lie.FairwaySand, Lie.GreensideSand, Lie.Recovery, Lie.Green];

    private Guid _roundId;
    private HoleInfo _info = new(0, 0, 0, 0);
    private bool _loading;
    private bool _applyingDefaults;
    private bool _typeTouched;
    private bool _clubTouched;
    private bool _penaltyAutoSet;

    public int HoleNumber => _info.Number;
    public int Par => _info.Par;
    public int Yardage => _info.Yardage;
    public int EntrySequence { get; private set; }
    public bool IsNewPlaying { get; private set; }

    /// <summary>The shots shown were restored from an unsaved draft (the app was closed mid-hole).</summary>
    public bool IsRestoredDraft { get; private set; }

    public IReadOnlyList<Lie> StartLies => StartLieChoices;
    public IReadOnlyList<Lie> EndLies { get; } = Enum.GetValues<Lie>().Where(l => l != Lie.Tee).ToList();
    public IReadOnlyList<Club> Clubs { get; } = Enum.GetValues<Club>();
    public IReadOnlyList<ShotType> ShotTypes { get; } = Enum.GetValues<ShotType>();
    public IReadOnlyList<Direction> Directions { get; } = Enum.GetValues<Direction>();
    public IReadOnlyList<Shape> Shapes { get; } = Enum.GetValues<Shape>();

    // ---- the shot being entered ----
    [ObservableProperty] private Lie _startLie = Lie.Tee;
    [ObservableProperty] private int _startDistance;
    [ObservableProperty] private Club _club = Club.Driver;
    [ObservableProperty] private ShotType _shotType = ShotType.Tee;
    [ObservableProperty] private Lie _endLie = Lie.Fairway;
    [ObservableProperty] private int? _endDistance;
    [ObservableProperty] private Direction _direction = Direction.OnTarget;
    [ObservableProperty] private Shape? _shape;
    [ObservableProperty] private int _penaltyStrokes;

    public string StartUnit => DistanceUnits.For(StartLie);
    public string EndUnit => DistanceUnits.For(EndLie);
    public bool IsHoled => EndLie == Lie.Holed;

    /// <summary>Index in <see cref="Shots"/> of the shot being edited, or null when adding a new one.</summary>
    [ObservableProperty] private int? _editingIndex;

    // ---- the hole so far ----
    public ObservableCollection<ShotItem> Shots { get; } = [];

    /// <summary>Problems with the shots entered (the hole not being holed yet is not a problem while entering; see <see cref="IsComplete"/>).</summary>
    public ObservableCollection<string> Errors { get; } = [];

    /// <summary>Why the shot being entered was not accepted.</summary>
    public ObservableCollection<string> DraftErrors { get; } = [];

    /// <summary>Strokes so far including penalties.</summary>
    [ObservableProperty] private int _runningScore;

    /// <summary>True when the last shot holes out and the shots pass <see cref="ShotValidator"/>.</summary>
    [ObservableProperty] private bool _isComplete;

    /// <summary>The domain's derivation for the finished hole, or null while it is still in progress.</summary>
    [ObservableProperty] private HoleSummary? _summary;

    public async Task LoadAsync(HoleArgs args)
    {
        _roundId = args.RoundId;
        var ctx = await RoundContext.LoadAsync(db, args.RoundId);
        if (ctx is null)
        {
            ErrorMessage = UiText.RoundNotFound;
            return;
        }
        if (ctx.Record.Round.EntryMode != EntryMode.ShotByShot)
        {
            ErrorMessage = UiText.WrongEntryModeShots;
            return;
        }

        var existing = args.EntrySequence is { } seq ? ctx.Record.Holes.FirstOrDefault(h => h.Hole.EntrySequence == seq) : null;
        _loading = true;
        try
        {
            Shots.Clear();
            if (existing is not null)
            {
                var info = ctx.Layout.FirstOrDefault(h => h.Number == existing.Hole.HoleNumber)
                    ?? new HoleInfo(existing.Hole.HoleNumber, existing.Hole.Par, 0, 0);
                _info = info;
                EntrySequence = existing.Hole.EntrySequence;
                IsNewPlaying = false;
                foreach (var shot in existing.Shots.OrderBy(s => s.Sequence))
                    Shots.Add(new ShotItem(shot.ToShot()));
            }
            else
            {
                var number = args.HoleNumber ?? ctx.SuggestNextHole();
                var info = ctx.Layout.FirstOrDefault(h => h.Number == number);
                if (info is null)
                {
                    ErrorMessage = UiText.HoleNotOnCourse;
                    return;
                }
                _info = info;
                EntrySequence = ctx.NextEntrySequence;
                IsNewPlaying = true;
            }
            EditingIndex = null;

            // Shots entered but not saved when the app last closed come back, as long as they are for this hole.
            IsRestoredDraft = false;
            var draft = await db.Drafts.GetAsync(_roundId, EntrySequence);
            if (draft is not null && draft.HoleNumber == _info.Number)
            {
                Shots.Clear();
                foreach (var shot in draft.Shots)
                    Shots.Add(new ShotItem(shot));
                IsRestoredDraft = true;
            }
            PrefillNext();
        }
        finally
        {
            _loading = false;
        }
        OnPropertyChanged(nameof(IsRestoredDraft));
        OnPropertyChanged(nameof(HoleNumber));
        OnPropertyChanged(nameof(Par));
        OnPropertyChanged(nameof(Yardage));
        OnPropertyChanged(nameof(EntrySequence));
        OnPropertyChanged(nameof(IsNewPlaying));
        OnPropertyChanged(nameof(StartUnit));
        OnPropertyChanged(nameof(EndUnit));
        OnPropertyChanged(nameof(IsHoled));
        Recompute();
    }

    // ---- prefill ----

    /// <summary>Sets the draft for the next shot from the previous shot's end.</summary>
    private void PrefillNext()
    {
        var wasLoading = _loading;
        _loading = true;
        _applyingDefaults = true;
        try
        {
            _typeTouched = _clubTouched = _penaltyAutoSet = false;
            Direction = Direction.OnTarget;
            Shape = null;
            PenaltyStrokes = 0;
            EndDistance = null;

            if (Shots.Count == 0)
            {
                StartLie = Lie.Tee;
                StartDistance = _info.Yardage;
                ShotType = ShotType.Tee;
                Club = _info.Par >= 4 ? Club.Driver : Club.Iron7;
            }
            else
            {
                var last = Shots[^1].Shot;
                switch (last.EndLie)
                {
                    case Lie.OutOfBounds:
                        // Stroke and distance: play again from the same spot (a re-tee after an out-of-bounds drive).
                        StartLie = last.StartLie;
                        StartDistance = last.StartDistance;
                        ShotType = last.Type;
                        Club = last.Club;
                        break;
                    case Lie.PenaltyArea:
                        StartLie = Lie.Rough;
                        StartDistance = last.EndDistance ?? last.StartDistance;
                        ApplyDefaultsForStartLie();
                        break;
                    case Lie.Holed:
                        // The hole is finished; leave the draft alone.
                        break;
                    default:
                        StartLie = last.EndLie;
                        StartDistance = last.EndDistance ?? 0;
                        ApplyDefaultsForStartLie();
                        break;
                }
            }
            EndLie = StartLie == Lie.Green ? Lie.Holed : Lie.Fairway;
            if (EndLie == Lie.Holed)
                EndDistance = null;
        }
        finally
        {
            _applyingDefaults = false;
            _loading = wasLoading;
        }
        OnPropertyChanged(nameof(StartUnit));
        OnPropertyChanged(nameof(EndUnit));
        OnPropertyChanged(nameof(IsHoled));
    }

    /// <summary>Suggests the shot type and club for the lie the shot starts from, unless the golfer already chose them.</summary>
    private void ApplyDefaultsForStartLie()
    {
        var wasApplying = _applyingDefaults;
        _applyingDefaults = true;
        try
        {
            if (!_typeTouched)
                ShotType = StartLie switch
                {
                    Lie.Tee => ShotType.Tee,
                    Lie.Green => ShotType.Putt,
                    Lie.GreensideSand or Lie.FairwaySand => ShotType.Bunker,
                    Lie.Recovery => ShotType.Recovery,
                    _ => ShotType.Approach,
                };
            if (!_clubTouched)
                Club = StartLie switch
                {
                    Lie.Green => Club.Putter,
                    Lie.Tee => _info.Par >= 4 ? Club.Driver : Club.Iron7,
                    Lie.GreensideSand => Club.SandWedge,
                    _ => Club == Club.Putter || Club == Club.Driver ? Club.Iron7 : Club,
                };
        }
        finally
        {
            _applyingDefaults = wasApplying;
        }
    }

    partial void OnStartLieChanged(Lie oldValue, Lie newValue)
    {
        OnPropertyChanged(nameof(StartUnit));
        if (_loading || _applyingDefaults)
            return;
        // Moving on or off the green changes the unit. Keep the physical distance (15 yd becomes 45 ft) rather than the digits,
        // so a distance typed before choosing the lie is not silently wrong and nothing the golfer typed is thrown away.
        StartDistance = DistanceUnits.Convert(StartDistance, oldValue, newValue);
        ApplyDefaultsForStartLie();
    }

    partial void OnClubChanged(Club value)
    {
        if (!_applyingDefaults && !_loading)
            _clubTouched = true;
    }

    partial void OnShotTypeChanged(ShotType value)
    {
        if (!_applyingDefaults && !_loading)
            _typeTouched = true;
    }

    partial void OnEndLieChanged(Lie oldValue, Lie newValue)
    {
        OnPropertyChanged(nameof(EndUnit));
        OnPropertyChanged(nameof(IsHoled));
        if (newValue == Lie.Holed)
            EndDistance = null;
        if (_loading || _applyingDefaults)
            return;
        if (EndDistance is { } typed && newValue != Lie.Holed && oldValue != Lie.Holed)
            EndDistance = DistanceUnits.Convert(typed, oldValue, newValue); // same physical distance in the new unit

        // Into a penalty area or out of bounds costs a stroke by default; the golfer can change it.
        if (newValue is Lie.PenaltyArea or Lie.OutOfBounds)
        {
            if (PenaltyStrokes == 0)
            {
                PenaltyStrokes = 1;
                _penaltyAutoSet = true;
            }
        }
        else if (_penaltyAutoSet && PenaltyStrokes == 1)
        {
            PenaltyStrokes = 0;
            _penaltyAutoSet = false;
        }
    }

    partial void OnPenaltyStrokesChanged(int value)
    {
        if (!_loading && !_applyingDefaults && value != 1)
            _penaltyAutoSet = false;
    }

    // ---- commands ----

    /// <summary>Adds the shot being entered (or replaces the one being edited), then prefills the next. The hole so far is saved as a
    /// draft before the command completes.</summary>
    [RelayCommand]
    private async Task AddShotAsync()
    {
        DraftErrors.Clear();
        var index = EditingIndex ?? Shots.Count;
        var shot = new Shot(index + 1, StartLie, StartDistance, Club, ShotType, EndLie, IsHoled ? null : EndDistance, Direction, Shape, PenaltyStrokes);

        var candidate = Shots.Select(s => s.Shot).ToList();
        if (EditingIndex is { } edit)
            candidate[edit] = shot;
        else
            candidate.Add(shot);

        // The hole not being holed yet is fine while entering; everything else blocks the shot.
        var blocking = ShotValidator.Validate(candidate).Where(e => e != ShotError.NotHoled).ToList();
        if (blocking.Count > 0)
        {
            foreach (var error in blocking)
                DraftErrors.Add(UiText.Describe(error));
            return;
        }

        if (EditingIndex is { } at)
            Shots[at] = new ShotItem(shot);
        else
            Shots.Add(new ShotItem(shot));
        EditingIndex = null;
        PrefillNext();
        Recompute();
        await SaveDraftAsync();
    }

    [RelayCommand]
    private void EditShot(ShotItem item)
    {
        var index = Shots.IndexOf(item);
        if (index < 0)
            return;
        var wasLoading = _loading;
        _loading = true;
        try
        {
            var s = item.Shot;
            StartLie = s.StartLie; StartDistance = s.StartDistance; Club = s.Club; ShotType = s.Type; EndLie = s.EndLie;
            EndDistance = s.EndDistance; Direction = s.Direction; Shape = s.Shape; PenaltyStrokes = s.PenaltyStrokes;
            // The golfer's recorded club and type are explicit, so later changes to the lie must not suggest over them.
            _clubTouched = _typeTouched = true;
            _penaltyAutoSet = false;
            EditingIndex = index;
        }
        finally
        {
            _loading = wasLoading;
        }
        DraftErrors.Clear();
        OnPropertyChanged(nameof(StartUnit));
        OnPropertyChanged(nameof(EndUnit));
        OnPropertyChanged(nameof(IsHoled));
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingIndex = null;
        DraftErrors.Clear();
        PrefillNext();
    }

    [RelayCommand]
    private async Task DeleteShotAsync(ShotItem item)
    {
        var index = Shots.IndexOf(item);
        if (index < 0)
            return;
        Shots.RemoveAt(index);
        for (var i = 0; i < Shots.Count; i++)
            Shots[i] = new ShotItem(Shots[i].Shot with { Sequence = i + 1 });
        EditingIndex = null;
        DraftErrors.Clear();
        PrefillNext();
        Recompute();
        await SaveDraftAsync();
    }

    [RelayCommand]
    private async Task UndoLastShotAsync()
    {
        if (Shots.Count > 0)
            await DeleteShotAsync(Shots[^1]);
    }

    private void Recompute()
    {
        var shots = Shots.Select(s => s.Shot).ToList();
        RunningScore = HoleSummaryDeriver.Strokes(shots);

        Errors.Clear();
        var errors = shots.Count == 0 ? [] : ShotValidator.Validate(shots);
        foreach (var error in errors.Where(e => e != ShotError.NotHoled))
            Errors.Add(UiText.Describe(error));

        IsComplete = shots.Count > 0 && errors.Count == 0;
        Summary = IsComplete ? HoleSummaryDeriver.Derive(_info, EntrySequence, shots) : null;
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Writes the shots entered so far so the hole survives the app closing. A failure to write a draft never interrupts entry.</summary>
    private async Task SaveDraftAsync()
    {
        try
        {
            await db.Drafts.SaveAsync(_roundId, EntrySequence, _info.Number, Shots.Select(s => s.Shot).ToList());
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    private async Task ClearDraftAsync()
    {
        try
        {
            await db.Drafts.ClearAsync(_roundId, EntrySequence);
        }
        catch (Exception)
        {
            // A leftover draft for a saved hole is harmless: it holds the same shots.
        }
    }

    /// <summary>Stores the hole (summary derived from the shots), clears its draft and returns to the round.</summary>
    [RelayCommand(CanExecute = nameof(IsComplete))]
    private async Task SaveAsync()
    {
        Recompute();
        if (!IsComplete)
            return;
        var record = HoleRecord.FromShots(_roundId, _info, EntrySequence, Shots.Select(s => s.Shot).ToList());
        if (!await TryAsync(() => db.Rounds.SaveHoleAsync(_roundId, record), UiText.RoundNotFound))
            return;
        await ClearDraftAsync();
        await navigator.GoBackAsync();
    }

    [RelayCommand]
    private async Task DeleteHoleAsync()
    {
        if (!IsNewPlaying && !await TryAsync(() => db.Rounds.DeleteHoleAsync(_roundId, EntrySequence), UiText.RoundNotFound))
            return;
        await ClearDraftAsync();
        await navigator.GoBackAsync();
    }

    partial void OnIsCompleteChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
}
