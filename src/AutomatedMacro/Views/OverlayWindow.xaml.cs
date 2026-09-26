using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AutomatedMacro;

/// <summary>Mala obavijest uvijek na vrhu; klikovi prolaze kroz nju.</summary>
public partial class OverlayWindow : Window
{
    private bool _topCenter;

    public OverlayWindow(Color dot)
    {
        InitializeComponent();
        Dot.Fill = new SolidColorBrush(dot);
        SourceInitialized += (_, _) => Win32.MakeClickThrough(new WindowInteropHelper(this).Handle);
        SizeChanged += (_, _) => { if (_topCenter) PlaceTopCenter(); };
    }

    public void SetText(string title, string detail)
    {
        TitleText.Text = title;
        DetailText.Text = detail;
        DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowTopCenter()
    {
        _topCenter = true;
        Show();
        PlaceTopCenter();
    }

    private void PlaceTopCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + 8;
    }

    /// <summary>Postavi uz kursor (fizicki pikseli).</summary>
    public void MoveNear(int x, int y)
    {
        _topCenter = false;
        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x + 18, y + 22, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }
}
