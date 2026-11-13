using CustomGolfStats.Domain.Stats;

namespace CustomGolfStats.App.Views;

/// <summary>Route.OverallStats: binds <see cref="OverallStatsViewModel"/>. Numbers come from the domain's SeasonStats.</summary>
public sealed partial class OverallStatsPage : RoutePage
{
    public OverallStatsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public OverallStatsViewModel ViewModel { get; } = Resolve<OverallStatsViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync();

    public static IReadOnlyList<Bar> Bars18(SeasonSection? season) => Display.Bars(season?.Histogram18);
    public static IReadOnlyList<Bar> Bars9(SeasonSection? season) => Display.Bars(season?.Histogram9);
    public static string HolesText(OverallTiles? tiles) => tiles is null ? "To par" : $"To par over {Display.Holes(tiles.HolesPlayed)}";
    public static string ResultLine(DateOnly playedOn, int holes, int putts, Ratio gir) =>
        $"{Display.Date(playedOn)}  ·  {Display.Holes(holes)}  ·  {putts} putt{(putts == 1 ? "" : "s")}  ·  GIR {Display.Percent(gir)}";
}
