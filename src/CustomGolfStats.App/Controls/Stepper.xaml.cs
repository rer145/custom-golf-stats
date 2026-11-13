using System.Globalization;
using Microsoft.UI.Xaml.Automation;

namespace CustomGolfStats.App.Controls;

/// <summary>Label, a − and a + button, and the value in mono. <see cref="Value"/> binds two-way to an int view model property.</summary>
public sealed partial class Stepper : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(Stepper), new PropertyMetadata(0, (d, _) => ((Stepper)d).Refresh()));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(Stepper), new PropertyMetadata("", (d, _) => ((Stepper)d).Refresh()));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(Stepper), new PropertyMetadata(0, (d, _) => ((Stepper)d).Refresh()));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(Stepper), new PropertyMetadata(int.MaxValue, (d, _) => ((Stepper)d).Refresh()));

    public Stepper()
    {
        InitializeComponent();
        Refresh();
    }

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    private void OnMinus(object sender, RoutedEventArgs e)
    {
        if (Value > Minimum)
            Value--;
    }

    private void OnPlus(object sender, RoutedEventArgs e)
    {
        if (Value < Maximum)
            Value++;
    }

    private void Refresh()
    {
        if (LabelText is null)
            return; // before InitializeComponent
        LabelText.Text = Label;
        ValueText.Text = Value.ToString(CultureInfo.InvariantCulture);
        MinusButton.IsEnabled = Value > Minimum;
        PlusButton.IsEnabled = Value < Maximum;
        AutomationProperties.SetName(MinusButton, $"Decrease {Label}");
        AutomationProperties.SetName(PlusButton, $"Increase {Label}");
        // Screen readers hear the whole field, e.g. "Putts 2".
        AutomationProperties.SetName(this, $"{Label} {ValueText.Text}");
        AutomationProperties.SetName(ValueText, $"{Label} {ValueText.Text}");
    }
}
