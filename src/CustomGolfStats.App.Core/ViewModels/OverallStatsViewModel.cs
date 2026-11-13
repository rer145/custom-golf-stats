using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CustomGolfStats.App.Core.Platform;
using CustomGolfStats.App.Core.Storage;
using CustomGolfStats.Domain.Stats;

namespace CustomGolfStats.App.Core.ViewModels;

/// <summary>The compact season summary (the Overall section) over the rounds on this device, using the domain's stats for a chosen year.
/// Rounds that are not 9 or 18 holes are left out of the 9/18 figures by the domain, as everywhere else.</summary>
public sealed partial class OverallStatsViewModel(LocalDb db, IClock clock) : ViewModelBase
{
    private IReadOnlyList<RoundData> _all = [];

    public ObservableCollection<int> Years { get; } = [];
    public ObservableCollection<SeasonResultRow> Results { get; } = [];

    [ObservableProperty] private int _selectedYear;
    [ObservableProperty] private OverallTiles? _tiles;
    [ObservableProperty] private SeasonSection? _season;

    /// <summary>"84.2", or "-" when there are no 18-hole rounds.</summary>
    [ObservableProperty] private string _scoringAverage18Text = "-";
    [ObservableProperty] private string _toParText = "E";
    [ObservableProperty] private int _roundsInYear;
    [ObservableProperty] private double? _handicapIndex;
    [ObservableProperty] private DateTime? _handicapEffectiveOn;

    public async Task LoadAsync()
    {
        var rows = await db.Rounds.ListAsync();
        var data = new List<RoundData>();
        foreach (var row in rows)
            if (await db.Rounds.GetAsync(row.Id) is { } record)
                data.Add(record.ToRoundData());
        _all = data;

        var handicap = await db.Handicap.LatestAsync();
        HandicapIndex = handicap?.Index;
        HandicapEffectiveOn = handicap?.EffectiveOn;

        Years.Clear();
        foreach (var year in data.Select(r => r.PlayedOn.Year).Distinct().OrderDescending())
            Years.Add(year);
        var current = clock.UtcNow.LocalDateTime.Year;
        SelectedYear = Years.Contains(current) ? current : Years.Count > 0 ? Years[0] : current;
        Recompute();
    }

    partial void OnSelectedYearChanged(int value) => Recompute();

    private void Recompute()
    {
        var year = SeasonStats.ForYear(_all, SelectedYear);
        RoundsInYear = year.Count;
        Tiles = SeasonStats.Overall(year);
        Season = SeasonStats.Season(year);
        ScoringAverage18Text = Tiles.ScoringAverage18.Value is { } avg ? avg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-";
        ToParText = UiText.ToPar(Tiles.ToPar);
        Results.Clear();
        foreach (var row in SeasonStats.Results(year))
            Results.Add(row);
    }
}
