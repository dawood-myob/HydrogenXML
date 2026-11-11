using System.Windows;

namespace HydrogenXML.Views;

public partial class PromptDialog : Window
{
    private readonly bool _useCombo;

    public PromptDialog(string title, string label, string defaultValue, IEnumerable<string>? choices)
    {
        InitializeComponent();
        Title = title;
        LabelText.Text = label;
        var list = choices?.ToList();
        _useCombo = list is { Count: > 0 };
        if (_useCombo)
        {
            ValueBox.Visibility = Visibility.Collapsed;
            ValueCombo.Visibility = Visibility.Visible;
            ValueCombo.ItemsSource = list;
            ValueCombo.Text = defaultValue;
        }
        else
        {
            ValueBox.Text = defaultValue;
        }

        Loaded += (_, _) =>
        {
            if (_useCombo) ValueCombo.Focus();
            else
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            }
        };
    }

    public string Value { get; private set; } = "";

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = (_useCombo ? ValueCombo.Text : ValueBox.Text).Trim();
        DialogResult = true;
    }
}
