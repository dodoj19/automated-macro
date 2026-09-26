using System.Windows;
using System.Windows.Media;

namespace AutomatedMacro;

/// <summary>
/// Fluent tema boji kontrole sistemskom bojom naglaska (Windows postavke).
/// Ovdje tu obitelj boja zamijenimo ljubicastom, da cijeli program izgleda kao na skici.
/// </summary>
public static class ThemeAccent
{
    public static void Apply(ResourceDictionary resources)
    {
        var map = new Dictionary<Color, Color>(new RgbComparer())
        {
            [SystemColors.AccentColor] = Rgb(0x7C, 0x5C, 0xFF),
            [SystemColors.AccentColorLight1] = Rgb(0x8F, 0x74, 0xFF),
            [SystemColors.AccentColorLight2] = Rgb(0xA7, 0x8B, 0xFA),
            [SystemColors.AccentColorLight3] = Rgb(0xC4, 0xB5, 0xFD),
            [SystemColors.AccentColorDark1] = Rgb(0x6A, 0x4D, 0xE6),
            [SystemColors.AccentColorDark2] = Rgb(0x5A, 0x3F, 0xCC),
            [SystemColors.AccentColorDark3] = Rgb(0x4A, 0x33, 0xAA),
        };
        foreach (var d in resources.MergedDictionaries) Recolor(d, map);
    }

    private static void Recolor(ResourceDictionary d, Dictionary<Color, Color> map)
    {
        foreach (var key in d.Keys.Cast<object>().ToList())
        {
            switch (d[key])
            {
                case SolidColorBrush b when map.TryGetValue(b.Color, out var c):
                    var brush = new SolidColorBrush(Color.FromArgb(b.Color.A, c.R, c.G, c.B)) { Opacity = b.Opacity };
                    brush.Freeze();
                    d[key] = brush;
                    break;
                case Color col when map.TryGetValue(col, out var c2):
                    d[key] = Color.FromArgb(col.A, c2.R, c2.G, c2.B);
                    break;
            }
        }
        foreach (var m in d.MergedDictionaries) Recolor(m, map);
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private sealed class RgbComparer : IEqualityComparer<Color>
    {
        public bool Equals(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;
        public int GetHashCode(Color c) => (c.R << 16) | (c.G << 8) | c.B;
    }
}
