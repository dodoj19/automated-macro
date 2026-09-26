using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AutomatedMacro;

/// <summary>"#RRGGBB" -> SolidColorBrush. Parametar (npr. "0.18") = prozirnost.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly Dictionary<(string, double), SolidColorBrush> Cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hex = Palette.Normalize(value as string) ?? "#8B8D98";
        double opacity = parameter is string p && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var o) ? o : 1.0;
        if (!Cache.TryGetValue((hex, opacity), out var brush))
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            c.A = (byte)Math.Round(255 * opacity);
            brush = new SolidColorBrush(c);
            brush.Freeze();
            Cache[(hex, opacity)] = brush;
        }
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value != null) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Enum -> Visibility (vidljivo kad je vrijednost jednaka parametru).</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter as string ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Dubina u stablu -> lijevi uvlak retka.</summary>
public sealed class DepthToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness((value is int d ? d : 0) * GuideLines.Indent, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Prazan tekst -> Collapsed.</summary>
public sealed class TextToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        !string.IsNullOrEmpty(value as string) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Clicks (1/2) &lt;-&gt; CheckBox "Dvoklik".</summary>
public sealed class DoubleClickConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is 2;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? 2 : 1;
}

/// <summary>Stavka izbornika ciji natpis prati odabrani jezik.</summary>
public abstract class LocalizedItem : INotifyPropertyChanged
{
    private readonly string _text;
    private readonly bool _translate;

    protected LocalizedItem(string text, bool translate)
    {
        _text = text;
        _translate = translate;
        if (translate) Loc.LanguageChanged += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label => _translate ? Loc.T(_text) : _text;
}

public sealed class Choice(object value, string key, bool translate = true) : LocalizedItem(key, translate)
{
    public object Value { get; } = value;
}

public sealed class KeyPreset(string label, int vk, bool ctrl = false, bool shift = false, bool alt = false, bool translate = false)
    : LocalizedItem(label, translate)
{
    public int Vk { get; } = vk;
    public bool Ctrl { get; } = ctrl;
    public bool Shift { get; } = shift;
    public bool Alt { get; } = alt;

    public KeyStep ToStep() => new() { Name = Loc.T("Key"), Vk = Vk, Ctrl = Ctrl, Shift = Shift, Alt = Alt };
}

public static class Choices
{
    public static IReadOnlyList<Choice> Languages { get; } =
    [
        new(AppLanguage.Hr, "Hrvatski", translate: false),
        new(AppLanguage.En, "English", translate: false),
        new(AppLanguage.De, "Deutsch", translate: false),
    ];

    public static IReadOnlyList<Choice> MouseButtons { get; } =
    [
        new(MouseButtonKind.Left, "Btn_Left"),
        new(MouseButtonKind.Right, "Btn_Right"),
        new(MouseButtonKind.Middle, "Btn_Middle"),
    ];

    public static IReadOnlyList<Choice> TextModes { get; } =
    [
        new(TextMode.Fixed, "FixedText"),
        new(TextMode.Random, "Mode_Random"),
    ];

    public static IReadOnlyList<Choice> LoopModes { get; } =
    [
        new(true, "Loop_Infinite"),
        new(false, "Loop_Count"),
    ];

    public static IReadOnlyList<Choice> Charsets { get; } =
    [
        new(CharsetKind.LettersDigits, "Cs_LettersDigits"),
        new(CharsetKind.Letters, "Cs_Letters"),
        new(CharsetKind.Lowercase, "Cs_Lower"),
        new(CharsetKind.LowercaseDigits, "Cs_LowerDigits"),
        new(CharsetKind.Digits, "Cs_Digits"),
    ];

    public static IReadOnlyList<KeyPreset> Keys { get; } =
    [
        new("Enter", 0x0D), new("Tab", 0x09), new("Shift+Tab", 0x09, shift: true), new("Esc", 0x1B),
        new("Space", 0x20), new("Backspace", 0x08), new("Delete", 0x2E),
        new("Key_Up", 0x26, translate: true), new("Key_Down", 0x28, translate: true),
        new("Key_Left", 0x25, translate: true), new("Key_Right", 0x27, translate: true),
        new("Home", 0x24), new("End", 0x23), new("PageUp", 0x21), new("PageDown", 0x22), new("F5", 0x74),
        new("Ctrl+A", 0x41, ctrl: true), new("Ctrl+C", 0x43, ctrl: true), new("Ctrl+V", 0x56, ctrl: true),
        new("Ctrl+X", 0x58, ctrl: true), new("Ctrl+Z", 0x5A, ctrl: true), new("Ctrl+S", 0x53, ctrl: true),
        new("Ctrl+T", 0x54, ctrl: true), new("Ctrl+W", 0x57, ctrl: true), new("Alt+Tab", 0x09, alt: true),
    ];
}
