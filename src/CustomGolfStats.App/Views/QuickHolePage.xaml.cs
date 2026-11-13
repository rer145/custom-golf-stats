namespace CustomGolfStats.App.Views;

/// <summary>Route.QuickHole: binds <see cref="QuickHoleViewModel"/>. One thumb: steppers, flags and a GIR toggle with its suggestion.</summary>
public sealed partial class QuickHolePage : RoutePage
{
    public QuickHolePage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public QuickHoleViewModel ViewModel { get; } = Resolve<QuickHoleViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync((HoleArgs)parameter!);

    public static string ToParText(int toPar) => UiText.ToPar(toPar);

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (await ConfirmAsync($"Delete hole {ViewModel.HoleNumber}?", "This playing of the hole is removed from the round.", "Delete"))
            await ViewModel.DeleteCommand.ExecuteAsync(null);
    }
}
