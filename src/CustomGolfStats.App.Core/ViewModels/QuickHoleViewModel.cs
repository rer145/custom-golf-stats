using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.Domain;
using CustomGolfStats.Domain.Holes;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>Quick entry of one hole: score, putts, fairway and tee-shot flags, GIR (suggested, overridable), penalties, sand shots.
/// All rules come from the domain: <see cref="GirRule"/> and <see cref="HoleSummaryValidator"/>.</summary>
public sealed partial class QuickHoleViewModel(LocalDb db, INavigator navigator) : ViewModelBase
{
    private Guid _roundId;
    private bool _loading;
    private bool _applyingGir;

    public int Par { get; private set; }
    public int Yardage { get; private set; }
    public int StrokeIndex { get; private set; }
    public int HoleNumber { get; private set; }
    public int EntrySequence { get; private set; }
    public bool IsNewPlaying { get; private set; }

    /// <summary>Fairway and tee-shot direction apply (the domain rejects them on par 3s).</summary>
    public bool HasTeeShotFlags { get; private set; }

    [ObservableProperty] private int _score;
    [ObservableProperty] private int _putts;
    [ObservableProperty] private bool _fairwayHit;
    [ObservableProperty] private bool _teeShotLeft;
    [ObservableProperty] private bool _teeShotRight;
    [ObservableProperty] private bool _teeShotLong;
    [ObservableProperty] private bool _teeShotShort;
    [ObservableProperty] private bool _gir;
    [ObservableProperty] private int _penaltyStrokes;
    [ObservableProperty] private int _greensideSandShots;
    [ObservableProperty] private int _fairwaySandShots;

    /// <summary>True once the golfer has set GIR by hand to something other than the suggestion.</summary>
    [ObservableProperty] private bool _girOverridden;

    [ObservableProperty] private bool _canSave;

    public ObservableCollection<string> Errors { get; } = [];

    public int ToPar => Score - Par;
    public bool SuggestedGir => GirRule.Suggest(Par, Score, Putts);

    public async Task LoadAsync(HoleArgs args)
    {
        _roundId = args.RoundId;
        var ctx = await RoundContext.LoadAsync(db, args.RoundId);
        if (ctx is null)
        {
            ErrorMessage = UiText.RoundNotFound;
            return;
        }
        if (ctx.Record.Round.EntryMode != EntryMode.Quick)
        {
            ErrorMessage = UiText.WrongEntryModeQuick;
            return;
        }

        _loading = true;
        try
        {
            var existing = args.EntrySequence is { } seq ? ctx.Record.Holes.FirstOrDefault(h => h.Hole.EntrySequence == seq) : null;
            if (existing is not null)
            {
                var row = existing.Hole;
                var info = ctx.Layout.FirstOrDefault(h => h.Number == row.HoleNumber);
                HoleNumber = row.HoleNumber;
                EntrySequence = row.EntrySequence;
                Par = row.Par;
                Yardage = info?.Yardage ?? 0;
                StrokeIndex = info?.StrokeIndex ?? 0;
                IsNewPlaying = false;
                Score = row.Score; Putts = row.Putts; FairwayHit = row.FairwayHit; TeeShotLeft = row.TeeShotLeft; TeeShotRight = row.TeeShotRight;
                TeeShotLong = row.TeeShotLong; TeeShotShort = row.TeeShotShort; Gir = row.Gir; PenaltyStrokes = row.PenaltyStrokes;
                GreensideSandShots = row.GreensideSandShots; FairwaySandShots = row.FairwaySandShots;
                GirOverridden = row.Gir != GirRule.Suggest(row.Par, row.Score, row.Putts);
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
                HoleNumber = info.Number;
                EntrySequence = ctx.NextEntrySequence;
                Par = info.Par;
                Yardage = info.Yardage;
                StrokeIndex = info.StrokeIndex;
                IsNewPlaying = true;
                Score = info.Par; Putts = 2; FairwayHit = false; TeeShotLeft = TeeShotRight = TeeShotLong = TeeShotShort = false;
                PenaltyStrokes = 0; GreensideSandShots = 0; FairwaySandShots = 0;
                GirOverridden = false;
                Gir = SuggestedGir;
            }
        }
        finally
        {
            _loading = false;
        }

        HasTeeShotFlags = !HoleSummaryValidator.Validate(Summary() with { FairwayHit = true }).Contains(HoleSummaryError.TeeFlagsOnPar3);
        OnPropertyChanged(nameof(HoleNumber));
        OnPropertyChanged(nameof(Par));
        OnPropertyChanged(nameof(Yardage));
        OnPropertyChanged(nameof(StrokeIndex));
        OnPropertyChanged(nameof(EntrySequence));
        OnPropertyChanged(nameof(HasTeeShotFlags));
        OnPropertyChanged(nameof(IsNewPlaying));
        OnPropertyChanged(nameof(ToPar));
        Revalidate();
    }

    public HoleSummary Summary() => new(HoleNumber, EntrySequence, Par, Score, Putts, FairwayHit, TeeShotLeft, TeeShotRight, TeeShotLong,
        TeeShotShort, Gir, PenaltyStrokes, GreensideSandShots, FairwaySandShots);

    partial void OnScoreChanged(int value) => Changed(suggestGir: true);
    partial void OnPuttsChanged(int value) => Changed(suggestGir: true);
    partial void OnFairwayHitChanged(bool value) => Changed();
    partial void OnTeeShotLeftChanged(bool value) => Changed();
    partial void OnTeeShotRightChanged(bool value) => Changed();
    partial void OnTeeShotLongChanged(bool value) => Changed();
    partial void OnTeeShotShortChanged(bool value) => Changed();
    partial void OnPenaltyStrokesChanged(int value) => Changed();
    partial void OnGreensideSandShotsChanged(int value) => Changed();
    partial void OnFairwaySandShotsChanged(int value) => Changed();

    partial void OnGirChanged(bool value)
    {
        if (_loading || _applyingGir)
            return;
        GirOverridden = value != SuggestedGir;
        Revalidate();
    }

    private void Changed(bool suggestGir = false)
    {
        if (_loading)
            return;
        if (suggestGir && !GirOverridden)
        {
            _applyingGir = true;
            Gir = SuggestedGir;
            _applyingGir = false;
        }
        OnPropertyChanged(nameof(ToPar));
        Revalidate();
    }

    /// <summary>Goes back to the automatic GIR suggestion after an override.</summary>
    [RelayCommand]
    private void ResetGir()
    {
        GirOverridden = false;
        _applyingGir = true;
        Gir = SuggestedGir;
        _applyingGir = false;
        Revalidate();
    }

    private void Revalidate()
    {
        Errors.Clear();
        foreach (var error in HoleSummaryValidator.Validate(Summary()))
            Errors.Add(UiText.Describe(error));
        CanSave = Errors.Count == 0 && EntrySequence > 0;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Revalidate();
        if (!CanSave)
            return;
        var row = HoleRow.FromSummary(_roundId, Summary(), isDerived: false);
        await db.Rounds.SaveHoleAsync(_roundId, new HoleRecord(row, []));
        await navigator.GoBackAsync();
    }

    /// <summary>Removes this playing of the hole from the round.</summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (IsNewPlaying)
        {
            await navigator.GoBackAsync();
            return;
        }
        await db.Rounds.DeleteHoleAsync(_roundId, EntrySequence);
        await navigator.GoBackAsync();
    }
}
