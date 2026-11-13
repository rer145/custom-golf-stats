namespace CustomGolfStats.App.Views;

/// <summary>Route.CoursePicker: binds <see cref="CoursePickerViewModel"/>.</summary>
public sealed partial class CoursePickerPage : RoutePage
{
    public CoursePickerPage()
    {
        InitializeComponent();
    }

    public CoursePickerViewModel ViewModel { get; } = Resolve<CoursePickerViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync();

    private void OnLocalCourseClick(object sender, ItemClickEventArgs e) => ViewModel.SelectLocalCommand.Execute(e.ClickedItem);

    private void OnBack(object sender, RoutedEventArgs e) => _ = Resolve<INavigator>().GoBackAsync();
}
