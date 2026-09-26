using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AutomatedMacro;

/// <summary>Okomite linije hijerarhije lijevo od koraka unutar grupa.</summary>
public sealed class GuideLines : FrameworkElement
{
    public static readonly DependencyProperty DepthProperty = DependencyProperty.Register(
        nameof(Depth), typeof(int), typeof(GuideLines), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen LinePen = CreatePen();

    public int Depth { get => (int)GetValue(DepthProperty); set => SetValue(DepthProperty, value); }

    public const double Indent = 22;

    protected override void OnRender(DrawingContext dc)
    {
        for (int i = 0; i < Depth; i++)
        {
            double x = Math.Round(i * Indent + Indent / 2) + 0.5;
            dc.DrawLine(LinePen, new Point(x, 0), new Point(x, ActualHeight));
        }
    }

    private static Pen CreatePen()
    {
        var p = new Pen(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), 1);
        p.Freeze();
        return p;
    }
}

/// <summary>Minijatura ekrana (svih monitora) s oznakom tocke koju odabrani korak klika.</summary>
public sealed class ScreenPreview : FrameworkElement
{
    public static readonly DependencyProperty NodeProperty = DependencyProperty.Register(
        nameof(Node), typeof(MacroNode), typeof(ScreenPreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnNodeChanged));

    private static readonly Brush ScreenFill = Frozen(new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x24)));
    private static readonly Brush BarFill = Frozen(new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen ScreenPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x46)), 1));
    private static readonly Pen PrimaryPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x70)), 1));
    private static readonly Brush Accent = Frozen(new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)));
    private static readonly Brush AccentGlow = Frozen(new SolidColorBrush(Color.FromArgb(0x44, 0x7C, 0x5C, 0xFF)));
    private static readonly Pen AccentPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA)), 1.5));
    private static readonly Pen CrossPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xA7, 0x8B, 0xFA)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Brush Faint = Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)));

    public MacroNode? Node { get => (MacroNode?)GetValue(NodeProperty); set => SetValue(NodeProperty, value); }

    public ScreenPreview()
    {
        Loc.LanguageChanged += InvalidateVisual;
    }

    private static void OnNodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (ScreenPreview)d;
        if (e.OldValue is INotifyPropertyChanged o) o.PropertyChanged -= self.OnNodePropertyChanged;
        if (e.NewValue is INotifyPropertyChanged n) n.PropertyChanged += self.OnNodePropertyChanged;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var monitors = Win32.Monitors();
        if (monitors.Count == 0 || ActualWidth < 20 || ActualHeight < 20) return;

        int left = monitors.Min(m => m.Bounds.Left), top = monitors.Min(m => m.Bounds.Top);
        int right = monitors.Max(m => m.Bounds.Right), bottom = monitors.Max(m => m.Bounds.Bottom);
        const double pad = 6;
        double scale = Math.Min((ActualWidth - pad * 2) / (right - left), (ActualHeight - pad * 2) / (bottom - top));
        double ox = (ActualWidth - (right - left) * scale) / 2, oy = (ActualHeight - (bottom - top) * scale) / 2;
        Point Map(int x, int y) => new(ox + (x - left) * scale, oy + (y - top) * scale);

        foreach (var (b, primary) in monitors)
        {
            var r = new Rect(Map(b.Left, b.Top), Map(b.Right, b.Bottom));
            r.Inflate(-1.5, -1.5);
            dc.DrawRoundedRectangle(ScreenFill, primary ? PrimaryPen : ScreenPen, r, 4, 4);
            // Ukrasni "prozor" kao na skici.
            dc.DrawRoundedRectangle(BarFill, null, new Rect(r.Left + 6, r.Top + 6, r.Width * 0.38, 4), 2, 2);
            dc.DrawRoundedRectangle(BarFill, null, new Rect(r.Left + 6, r.Top + 14, r.Width * 0.24, 4), 2, 2);
        }

        var node = Node;
        if (node?.Point is not { } p)
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var text = new FormattedText(Loc.T(node == null ? "Preview_SelectStep" : "Preview_NoPoint"),
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Faint, dpi);
            dc.DrawText(text, new Point((ActualWidth - text.Width) / 2, ActualHeight - text.Height - 4));
            return;
        }

        var a = Map(p.X, p.Y);
        dc.DrawLine(CrossPen, new Point(a.X, oy), new Point(a.X, oy + (bottom - top) * scale));
        dc.DrawLine(CrossPen, new Point(ox, a.Y), new Point(ox + (right - left) * scale, a.Y));

        if (node.Point2 is { } p2)
        {
            var b2 = Map(p2.X, p2.Y);
            dc.DrawLine(AccentPen, a, b2);
            dc.DrawEllipse(Accent, null, b2, 3, 3);
        }

        dc.DrawEllipse(AccentGlow, null, a, 9, 9);
        dc.DrawEllipse(Accent, null, a, 4, 4);
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
