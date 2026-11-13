using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CustomGolfStats.App.Core.Storage;

namespace CustomGolfStats.App.Core.ViewModels;

public sealed record ScorecardRow(int EntrySequence, int HoleNumber, int Par, int Yardage, int StrokeIndex, int Score, int ToPar,
    int Putts, bool FairwayHit, bool Gir, int PenaltyStrokes)
{
    public string ToParText => UiText.ToPar(ToPar);
}

/// <summary>The round's scorecard from local data: each hole played in order with the tee's yardage and stroke index, and totals.</summary>
public sealed partial class ScorecardViewModel(LocalDb db) : ViewModelBase
{
    public ObservableCollection<ScorecardRow> Rows { get; } = [];

    [ObservableProperty] private string _courseName = "";
    [ObservableProperty] private string _teeName = "";
    [ObservableProperty] private DateTime _playedOn;
    [ObservableProperty] private double? _rating;
    [ObservableProperty] private int? _slope;
    [ObservableProperty] private int _totalScore;
    [ObservableProperty] private int _totalPar;
    [ObservableProperty] private int _toPar;
    [ObservableProperty] private string _toParText = UiText.ToPar(0);
    [ObservableProperty] private int _totalPutts;
    [ObservableProperty] private int _totalYardage;

    public async Task LoadAsync(RoundArgs args)
    {
        var ctx = await RoundContext.LoadAsync(db, args.RoundId);
        if (ctx is null)
        {
            ErrorMessage = UiText.RoundNotFound;
            return;
        }

        var round = ctx.Record.Round;
        CourseName = round.CourseName;
        TeeName = round.TeeName;
        PlayedOn = round.PlayedOn;
        Rating = ctx.Version?.Rating;
        Slope = ctx.Version?.Slope;

        Rows.Clear();
        foreach (var hole in ctx.Record.Holes.OrderBy(h => h.Hole.EntrySequence))
        {
            var s = hole.ToSummary();
            var info = ctx.Layout.FirstOrDefault(l => l.Number == s.HoleNumber);
            Rows.Add(new ScorecardRow(s.EntrySequence, s.HoleNumber, s.Par, info?.Yardage ?? 0, info?.StrokeIndex ?? 0, s.Score, s.ToPar,
                s.Putts, s.FairwayHit, s.Gir, s.PenaltyStrokes));
        }

        var data = ctx.Record.ToRoundData();
        TotalScore = data.Score;
        TotalPar = data.Par;
        ToPar = data.ToPar;
        ToParText = UiText.ToPar(data.ToPar);
        TotalPutts = data.Putts;
        TotalYardage = Rows.Sum(r => r.Yardage);
    }
}
