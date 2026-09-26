using System.Security.Cryptography;

namespace AutomatedMacro;

/// <summary>Nasumicni tekst koji se unutar jednog pokretanja programa nikad ne ponovi.</summary>
public static class TextGenerator
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";

    private static readonly HashSet<string> Used = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    public static string Alphabet(CharsetKind kind) => kind switch
    {
        CharsetKind.Letters => Upper + Lower,
        CharsetKind.Lowercase => Lower,
        CharsetKind.LowercaseDigits => Lower + Digits,
        CharsetKind.Digits => Digits,
        _ => Upper + Lower + Digits,
    };

    public static string Unique(int minLength, int maxLength, CharsetKind charset)
    {
        int min = Math.Clamp(Math.Min(minLength, maxLength), TextStep.MinAllowed, TextStep.MaxAllowed);
        int max = Math.Clamp(Math.Max(minLength, maxLength), TextStep.MinAllowed, TextStep.MaxAllowed);
        var alphabet = Alphabet(charset);

        lock (Gate)
        {
            for (int attempt = 0; attempt < 10_000; attempt++)
            {
                // Ako su kratke kombinacije potrosene, postupno produlji.
                int bump = attempt / 1000;
                int len = RandomNumberGenerator.GetInt32(Math.Min(min + bump, max), max + 1);
                var s = RandomNumberGenerator.GetString(alphabet, len);
                if (Used.Add(s)) return s;
            }
        }
        throw new InvalidOperationException(Loc.T("Err_NoUnique"));
    }

    public static string ForStep(TextStep step) => step.Mode == TextMode.Fixed
        ? step.Text
        : step.Prefix + Unique(step.MinLength, step.MaxLength, step.Charset) + step.Suffix;
}
