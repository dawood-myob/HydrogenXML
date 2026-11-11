using System.Collections.ObjectModel;
using System.Windows;
using HydrogenXML.Core.Workspace;
using HydrogenXML.ViewModels;

namespace HydrogenXML.Views;

public partial class TransformSettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<ParameterSetting> _parameters;

    public TransformSettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        AutoBox.IsChecked = vm.AutoTransform;
        PrettyBox.IsChecked = vm.PrettyPrint;
        DelayBox.ItemsSource = vm.DelayChoices;
        DelayBox.Text = vm.AutoDelayMs.ToString();
        TemplateBox.Text = vm.TransformSettings.InitialTemplate ?? "";
        ModeBox.Text = vm.TransformSettings.InitialMode ?? "";
        _parameters = new ObservableCollection<ParameterSetting>(
            vm.TransformSettings.Parameters.Select(p => new ParameterSetting { Name = p.Name, Value = p.Value }));
        ParamGrid.ItemsSource = _parameters;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ParamGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        if (!int.TryParse(DelayBox.Text, out var delay) || delay < 100 || delay > 60000)
        {
            MessageBox.Show(this, "The delay must be a number between 100 and 60000 milliseconds.", "HydrogenXML",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _vm.AutoDelayMs = delay;
        _vm.PrettyPrint = PrettyBox.IsChecked == true;
        _vm.AutoTransform = AutoBox.IsChecked == true;
        _vm.TransformSettings.InitialTemplate = string.IsNullOrWhiteSpace(TemplateBox.Text) ? null : TemplateBox.Text.Trim();
        _vm.TransformSettings.InitialMode = string.IsNullOrWhiteSpace(ModeBox.Text) ? null : ModeBox.Text.Trim();
        _vm.TransformSettings.Parameters = _parameters.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        DialogResult = true;
    }
}
