using CustomGolfStats.App.Platform;

namespace CustomGolfStats.App.Views;

/// <summary>Route.Round: binds <see cref="RoundViewModel"/>. Holes in the order they were played; any hole can be played again.</summary>
public sealed partial class RoundPage : RoutePage
{
    public RoundPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public RoundViewModel ViewModel { get; } = Resolve<RoundViewModel>();

    protected override async Task LoadAsync(object? parameter)
    {
        await ViewModel.LoadAsync((RoundArgs)parameter!);
        OtherHoleMenu.Items.Clear();
        foreach (var hole in ViewModel.HoleOptions)
        {
            var item = new MenuFlyoutItem { Text = $"Hole {hole.Number}  ·  par {hole.Par}" };
            var number = hole.Number;
            item.Click += (_, _) => ViewModel.AddHoleCommand.Execute(number);
            OtherHoleMenu.Items.Add(item);
        }
        Bindings.Update();
        await Resolve<IMotionPermission>().PrepareAsync();
    }

    public static string HoleLine(int par, int putts) => $"Par {par}  ·  {putts} putt{(putts == 1 ? "" : "s")}";

    public static string HoleFlags(bool gir, bool fairway, int shots, bool derived)
    {
        var parts = new List<string> { gir ? "GIR" : "Missed green", fairway ? "Fairway" : "" };
        if (derived)
            parts.Add($"{shots} shots");
        return string.Join("  ·  ", parts.Where(p => p.Length > 0));
    }

    private void OnAddSuggested(object sender, RoutedEventArgs e) => ViewModel.AddHoleCommand.Execute(null);

    private void OnHoleClick(object sender, ItemClickEventArgs e) => ViewModel.EditHoleCommand.Execute(e.ClickedItem);

    private async void OnDeleteHole(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HoleListItem hole })
            return;
        if (await ConfirmAsync($"Delete hole {hole.HoleNumber}?", "This playing of the hole and its shots are removed from the round.", "Delete"))
            await ViewModel.DeleteHoleCommand.ExecuteAsync(hole);
    }
}
