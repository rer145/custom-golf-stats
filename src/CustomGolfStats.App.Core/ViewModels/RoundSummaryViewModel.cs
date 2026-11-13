using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.Domain.Stats;
using RoundType = CustomGolfStats.Api.Client.RoundType;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>The finished (or in-progress) round: score, per-hole results, domain stats for this round, and the editable extras
/// (notes, round type, weather, duration, steps, distance). Every extra is optional.</summary>
public sealed partial class RoundSummaryViewModel(LocalDb db, INavigator navigator) : ViewModelBase
{
    private Guid _roundId;

    public ObservableCollection<HoleListItem> Holes { get; } = [];
    public IReadOnlyList<RoundType> RoundTypes { get; } = Enum.GetValues<RoundType>();
    public ObservableCollection<string> ExtrasErrors { get; } = [];

    [ObservableProperty] private string _courseName = "";
    [ObservableProperty] private string _teeName = "";
    [ObservableProperty] private DateTime _playedOn;
    [ObservableProperty] private int _holesPlayed;
    [ObservableProperty] private int _score;
    [ObservableProperty] private int _par;
    [ObservableProperty] private int _toPar;
    [ObservableProperty] private string _toParText = UiText.ToPar(0);
    [ObservableProperty] private int _putts;
    [ObservableProperty] private bool _isFinished;

    /// <summary>The domain's one-row summary of this round (GIR, driving accuracy, par 3/4/5 to par), or null with no holes.</summary>
    [ObservableProperty] private SeasonResultRow? _result;

    // ---- editable extras ----
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private RoundType _roundType;
    [ObservableProperty] private string? _weatherConditions;
    [ObservableProperty] private double? _temperatureF;
    [ObservableProperty] private double? _windSpeedMph;
    [ObservableProperty] private int? _windDirectionDegrees;
    [ObservableProperty] private int? _durationMinutes;
    [ObservableProperty] private int? _stepCount;
    [ObservableProperty] private double? _distanceMiles;

    public async Task LoadAsync(RoundArgs args)
    {
        _roundId = args.RoundId;
        var record = await db.Rounds.GetAsync(args.RoundId);
        if (record is null)
        {
            ErrorMessage = UiText.RoundNotFound;
            return;
        }

        var r = record.Round;
        CourseName = r.CourseName;
        TeeName = r.TeeName;
        PlayedOn = r.PlayedOn;
        IsFinished = r.FinishedAt is not null;
        Notes = r.Notes; RoundType = r.RoundType; WeatherConditions = r.WeatherConditions; TemperatureF = r.TemperatureF;
        WindSpeedMph = r.WindSpeedMph; WindDirectionDegrees = r.WindDirectionDegrees; DurationMinutes = r.DurationMinutes;
        StepCount = r.StepCount; DistanceMiles = r.DistanceMiles;

        Holes.Clear();
        foreach (var hole in record.Holes.OrderBy(h => h.Hole.EntrySequence))
        {
            var s = hole.ToSummary();
            Holes.Add(new HoleListItem(s.EntrySequence, s.HoleNumber, s.Par, s.Score, s.ToPar, s.Putts, s.Gir, s.FairwayHit, hole.Hole.IsDerived, hole.Shots.Count));
        }

        var data = record.ToRoundData();
        HolesPlayed = Holes.Count;
        Score = data.Score;
        Par = data.Par;
        ToPar = data.ToPar;
        ToParText = UiText.ToPar(data.ToPar);
        Putts = data.Putts;
        Result = data.Holes.Count == 0 ? null : SeasonStats.Results([data])[0];
    }

    /// <summary>Saves the extras after checking them; the round becomes dirty so the edit syncs.</summary>
    [RelayCommand]
    private async Task SaveExtrasAsync()
    {
        ExtrasErrors.Clear();
        if (WindSpeedMph is < 0 || DurationMinutes is < 0 || StepCount is < 0 || DistanceMiles is < 0)
            ExtrasErrors.Add(UiText.NegativeNumber);
        if (WindDirectionDegrees is < 0 or > 359)
            ExtrasErrors.Add(UiText.WindDirectionInvalid);
        if (ExtrasErrors.Count > 0)
            return;

        var notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        var conditions = string.IsNullOrWhiteSpace(WeatherConditions) ? null : WeatherConditions.Trim();
        var saved = await db.Rounds.UpdateHeaderAsync(_roundId, r =>
        {
            r.Notes = notes; r.RoundType = RoundType; r.WeatherConditions = conditions; r.TemperatureF = TemperatureF;
            r.WindSpeedMph = WindSpeedMph; r.WindDirectionDegrees = WindDirectionDegrees; r.DurationMinutes = DurationMinutes;
            r.StepCount = StepCount; r.DistanceMiles = DistanceMiles;
        });
        if (!saved)
            ErrorMessage = UiText.RoundNotFound;
    }

    /// <summary>Deletes the round (a tombstone that syncs) and returns home. The view should confirm first.</summary>
    [RelayCommand]
    private async Task DeleteRoundAsync()
    {
        await db.Rounds.MarkDeletedAsync(_roundId);
        await navigator.NavigateAsync(Route.Home);
    }

    [RelayCommand]
    private Task OpenScorecardAsync() => navigator.NavigateAsync(Route.Scorecard, new RoundArgs(_roundId));

    [RelayCommand]
    private Task DoneAsync() => navigator.NavigateAsync(Route.Home);
}
