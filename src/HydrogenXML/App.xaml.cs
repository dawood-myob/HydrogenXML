using System.Windows;
using HydrogenXML.Editor;
using HydrogenXML.ViewModels;

namespace HydrogenXML;

public partial class App : Application
{
	private static readonly Uri DarkUri = new("pack://application:,,,/HydrogenXML;component/Themes/Dark.xaml");
	private static readonly Uri LightUri = new("pack://application:,,,/HydrogenXML;component/Themes/Light.xaml");

	private static ResourceDictionary? _palette;

	public static MainViewModel ViewModel { get; private set; } = null!;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		DispatcherUnhandledException += (_, args) =>
		{
			MessageBox.Show(args.Exception.ToString(), "HydrogenXML — unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
			args.Handled = true;
		};

		ViewModel = new MainViewModel();
		ApplyTheme(ViewModel.Theme);
		RegisterSharedStyles();
		var window = new MainWindow(ViewModel);
		MainWindow = window;
		ViewModel.Initialize();
		window.ApplyLayout();
		window.Show();
	}

	// Built after the Fluent theme is merged so the style can inherit its Button template (a plain XAML style would drop it).
	private void RegisterSharedStyles()
	{
		var style = new Style(typeof(System.Windows.Controls.Button), TryFindResource(typeof(System.Windows.Controls.Button)) as Style);
		style.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(8, 2, 8, 2)));
		style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)));
		style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 24.0));
		Resources["MiniButton"] = style;
	}

	/// <summary>Switches between
	public static void ApplyTheme(string name)
	{
		var dark = !string.Equals(name, "Light", StringComparison.OrdinalIgnoreCase);
		var app = Current;
		if (app == null) return;

		var dict = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
		// The Fluent ThemeMode inserts its own dictionaries into this list, so the palette must be tracked by reference, not by index.
		var merged = app.Resources.MergedDictionaries;
		if (_palette != null) merged.Remove(_palette);
		merged.Add(dict);
		_palette = dict;

#pragma warning disable WPF0001 // Fluent theme support is experimental in WPF but is the supported way to theme built-in controls.
		app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
		foreach (Window w in app.Windows) w.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

		SyntaxTheme.Apply(dark);
	}
}
