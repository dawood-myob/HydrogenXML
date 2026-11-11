using System.Windows;
using Microsoft.Win32;
using HydrogenXML.Views;

namespace HydrogenXML.Infrastructure;

public enum YesNoCancel
{
    Yes,
    No,
    Cancel
}

public static class Dialogs
{
    public const string XslFilter = "XSLT stylesheets (*.xsl;*.xslt)|*.xsl;*.xslt|XML files (*.xml)|*.xml|All files (*.*)|*.*";
    public const string XmlFilter = "XML files (*.xml)|*.xml|All files (*.*)|*.*";
    public const string WorkspaceFilter = "HydrogenXML workspace (*.hydrogen)|*.hydrogen";

    private static Window? Owner => Application.Current?.MainWindow;

    public static string[]? OpenFiles(string title, string filter, bool multi = true)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter, Multiselect = multi, CheckFileExists = true };
        return dlg.ShowDialog(Owner) == true ? dlg.FileNames : null;
    }

    public static string? SaveFile(string title, string filter, string? fileName = null, string? initialDirectory = null)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = fileName ?? "",
            OverwritePrompt = true,
            AddExtension = true
        };
        if (initialDirectory != null && System.IO.Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public static YesNoCancel AskSave(string message, string title = "HydrogenXML")
    {
        var r = MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return r switch
        {
            MessageBoxResult.Yes => YesNoCancel.Yes,
            MessageBoxResult.No => YesNoCancel.No,
            _ => YesNoCancel.Cancel
        };
    }

    public static bool Confirm(string message, string title = "HydrogenXML") =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Info(string message, string title = "HydrogenXML") =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(string message, string title = "HydrogenXML") =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static string? Prompt(string title, string label, string defaultValue = "", IEnumerable<string>? choices = null)
    {
        var dlg = new PromptDialog(title, label, defaultValue, choices) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}

/// <summary>Restarts a timer on every call so only the last request within the delay runs.</summary>
public sealed class Debouncer
{
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private Action? _action;

    public Debouncer()
    {
        _timer = new System.Windows.Threading.DispatcherTimer();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            var a = _action;
            _action = null;
            a?.Invoke();
        };
    }

    public void Run(TimeSpan delay, Action action)
    {
        _action = action;
        _timer.Stop();
        _timer.Interval = delay;
        _timer.Start();
    }

    public void Cancel()
    {
        _timer.Stop();
        _action = null;
    }
}
