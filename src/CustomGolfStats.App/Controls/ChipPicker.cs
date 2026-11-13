using System.Collections;
using CustomGolfStats.App.Views;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CustomGolfStats.App.Controls;

/// <summary>
/// One-tap choice from a short list (lies, shot types, directions...): every option is a 44 pt chip, the chosen one filled.
/// <see cref="SelectedItem"/> binds two-way to the view model property. With <see cref="AllowNone"/> tapping the chosen chip
/// clears the choice (for optional values such as shot shape).
/// </summary>
public sealed partial class ChipPicker : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ChipPicker), new PropertyMetadata(null, (d, _) => ((ChipPicker)d).Rebuild()));

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(ChipPicker), new PropertyMetadata(null, (d, _) => ((ChipPicker)d).SyncChecks()));

    private readonly WrapPanel _panel = new() { Spacing = 8 };
    private readonly List<(object Item, ToggleButton Chip)> _chips = [];
    private bool _syncing;

    public ChipPicker()
    {
        Content = _panel;
    }

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public bool AllowNone { get; set; }

    /// <summary>Keep every chip on one line (inside a horizontal ScrollViewer), for long lists such as clubs.</summary>
    public bool SingleRow { get => _panel.SingleRow; set => _panel.SingleRow = value; }

    private void Rebuild()
    {
        _panel.Children.Clear();
        _chips.Clear();
        if (ItemsSource is null)
            return;
        foreach (var item in ItemsSource)
        {
            var chip = new ToggleButton
            {
                Content = Display.Text(item),
                Style = (Style)Application.Current.Resources["BrandChipStyle"],
            };
            var captured = item;
            chip.Click += (_, _) => OnChipClicked(captured);
            _chips.Add((item, chip));
            _panel.Children.Add(chip);
        }
        SyncChecks();
    }

    private void OnChipClicked(object item)
    {
        if (Equals(SelectedItem, item))
        {
            if (AllowNone)
                SelectedItem = null;
            SyncChecks(); // a chip cannot be un-chosen by tapping it again unless the value is optional
            return;
        }
        SelectedItem = item;
    }

    private void SyncChecks()
    {
        if (_syncing)
            return;
        _syncing = true;
        foreach (var (item, chip) in _chips)
            chip.IsChecked = Equals(item, SelectedItem);
        _syncing = false;
    }
}
