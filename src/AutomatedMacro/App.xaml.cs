using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutomatedMacro;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Enter u jednorednom polju odmah potvrdjuje vrijednost.
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.KeyDownEvent, new KeyEventHandler((s, args) =>
        {
            if (args.Key == Key.Enter && s is TextBox { AcceptsReturn: false } tb)
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }));

        ThemeAccent.Apply(Resources);

        var settings = AppSettings.Load();
        settings.Language ??= Loc.Detect();
        Loc.Instance.SetLanguage(settings.Language.Value);

        var window = new MainWindow(settings);
        MainWindow = window;
        window.Show();

        var file = e.Args.FirstOrDefault(File.Exists) ?? (File.Exists(settings.LastFile) ? settings.LastFile : null);
        if (file != null) window.TryOpen(file, quiet: e.Args.Length == 0);
    }
}
