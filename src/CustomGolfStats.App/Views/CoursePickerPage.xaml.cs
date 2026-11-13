using CustomGolfStats.App.Core.Storage.Rows;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using ProviderSearchResultDto = CustomGolfStats.Api.Client.ProviderSearchResultDto;

namespace CustomGolfStats.App.Views;

/// <summary>Route.CoursePicker: binds <see cref="CoursePickerViewModel"/>. Saved, shared and directory courses in three sections.</summary>
public sealed partial class CoursePickerPage : RoutePage
{
    public CoursePickerPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.LocalResults.CollectionChanged += (_, _) => Bindings.Update();
        ViewModel.OnlineResults.CollectionChanged += (_, _) => Bindings.Update();
        ViewModel.ProviderResults.CollectionChanged += (_, _) => Bindings.Update();
    }

    public CoursePickerViewModel ViewModel { get; } = Resolve<CoursePickerViewModel>();

    protected override Task LoadAsync(object? parameter) => ViewModel.LoadAsync();

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
            ViewModel.SearchCommand.Execute(null);
    }

    private void OnLocalClick(object sender, ItemClickEventArgs e) => ViewModel.SelectLocalCommand.Execute(e.ClickedItem);

    private void OnOnlineClick(object sender, ItemClickEventArgs e) => ViewModel.SelectOnlineCommand.Execute(e.ClickedItem);

    private void OnToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CourseRow course })
            ViewModel.ToggleFavoriteCommand.Execute(course);
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProviderSearchResultDto result })
            ViewModel.ImportCommand.Execute(result);
    }
}
