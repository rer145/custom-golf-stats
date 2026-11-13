namespace CustomGolfStats.App.Views;

/// <summary>Route.ManualCourse: binds <see cref="ManualCourseViewModel"/>.</summary>
public sealed partial class ManualCoursePage : RoutePage
{
    public ManualCoursePage()
    {
        InitializeComponent();
    }

    public ManualCourseViewModel ViewModel { get; } = Resolve<ManualCourseViewModel>();

    protected override Task LoadAsync(object? parameter) => InitializeOnce();

    private bool _initialized;

    private Task InitializeOnce()
    {
        if (!_initialized)
            ViewModel.Initialize();
        _initialized = true;
        return Task.CompletedTask;
    }

    private void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();
}
