using CustomGolfStats.App.Core.Sync;

namespace CustomGolfStats.App.Views;

/// <summary>Route.Home: binds <see cref="HomeViewModel"/>. Opening the screen refreshes the list and starts a sync.</summary>
public sealed partial class HomePage : RoutePage
{
    public HomePage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.Rounds.CollectionChanged += (_, _) => Bindings.Update();
    }

    public HomeViewModel ViewModel { get; } = Resolve<HomeViewModel>();

    protected override async Task LoadAsync(object? parameter)
    {
        await ViewModel.LoadAsync();
        _ = ViewModel.SyncCommand.ExecuteAsync(null);
    }

    private void OnRoundClick(object sender, ItemClickEventArgs e) => ViewModel.OpenRoundCommand.Execute(e.ClickedItem);

    private void OnDismissNotice(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SyncNotice notice })
            ViewModel.DismissNoticeCommand.Execute(notice);
    }
}
