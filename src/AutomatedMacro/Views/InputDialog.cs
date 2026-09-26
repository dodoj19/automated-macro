using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutomatedMacro;

/// <summary>Jednostavan prozor za unos imena (nova procedura, preimenovanje, nova mapa).</summary>
public sealed class InputDialog : Window
{
    private readonly TextBox _box;

    private InputDialog(Window owner, string title, string label, string initial)
    {
        Owner = owner;
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)FindResource("PanelBgBrush");
        Foreground = (Brush)FindResource("TextBrush");

        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 10) });
        _box = new TextBox { Text = initial };
        panel.Children.Add(_box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };
        var ok = new Button { Content = Loc.T("OK"), IsDefault = true, MinWidth = 96, Style = (Style)FindResource("AccentButton") };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    public static string? Ask(Window owner, string title, string label, string initial = "")
    {
        var dialog = new InputDialog(owner, title, label, initial);
        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog._box.Text) ? dialog._box.Text.Trim() : null;
    }
}
