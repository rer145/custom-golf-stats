namespace CustomGolfStats.App.Views;

/// <summary>Route.ShotEntry: binds <see cref="ShotEntryViewModel"/>. Prefill, units, validation and the derived summary are the
/// view model's; this page only lays the choices out as one-tap chips and steppers.</summary>
public sealed partial class ShotEntryPage : RoutePage
{
    public ShotEntryPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.Shots.CollectionChanged += (_, _) => Bindings.Update();
    }

    public ShotEntryViewModel ViewModel { get; } = Resolve<ShotEntryViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync((HoleArgs)parameter!);

    public static Visibility EntryVisible(bool complete, int? editingIndex) =>
        !complete || editingIndex is not null ? Visibility.Visible : Visibility.Collapsed;

    private void OnShotClick(object sender, ItemClickEventArgs e) => ViewModel.EditShotCommand.Execute(e.ClickedItem);

    private void OnDeleteShot(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ShotItem item })
            ViewModel.DeleteShotCommand.Execute(item);
    }

    private async void OnDeleteHole(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync($"Delete hole {ViewModel.HoleNumber}?", "This playing of the hole and its shots are removed from the round.", "Delete"))
            await ViewModel.DeleteHoleCommand.ExecuteAsync(null);
    }
}
