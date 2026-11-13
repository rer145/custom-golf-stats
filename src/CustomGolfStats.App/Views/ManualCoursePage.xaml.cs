namespace CustomGolfStats.App.Views;

/// <summary>Route.ManualCourse: binds <see cref="ManualCourseViewModel"/>. Works offline; the course syncs later.</summary>
public sealed partial class ManualCoursePage : RoutePage
{
    private bool _initialized;

    public ManualCoursePage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public ManualCourseViewModel ViewModel { get; } = Resolve<ManualCourseViewModel>();

    protected override Task LoadAsync(object? parameter)
    {
        if (!_initialized)
            ViewModel.Initialize();
        _initialized = true;
        return Task.CompletedTask;
    }

    private void OnNine(object sender, RoutedEventArgs e) => ViewModel.SetHoleCountCommand.Execute(9);

    private void OnEighteen(object sender, RoutedEventArgs e) => ViewModel.SetHoleCountCommand.Execute(18);
}
