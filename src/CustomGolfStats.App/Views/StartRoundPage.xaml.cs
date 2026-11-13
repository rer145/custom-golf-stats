using DomEntryMode = CustomGolfStats.Domain.EntryMode;

namespace CustomGolfStats.App.Views;

/// <summary>Route.StartRound: binds <see cref="StartRoundViewModel"/>. Shot by shot is disabled with the reason when not allowed.</summary>
public sealed partial class StartRoundPage : RoutePage
{
    public StartRoundPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public StartRoundViewModel ViewModel { get; } = Resolve<StartRoundViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync((CourseArgs)parameter!);

    public static bool IsQuick(DomEntryMode mode) => mode == DomEntryMode.Quick;
    public static bool IsShotByShot(DomEntryMode mode) => mode == DomEntryMode.ShotByShot;

    private void OnQuickChecked(object sender, RoutedEventArgs e) => ViewModel.Mode = DomEntryMode.Quick;

    private void OnShotChecked(object sender, RoutedEventArgs e) => ViewModel.Mode = DomEntryMode.ShotByShot;
}
