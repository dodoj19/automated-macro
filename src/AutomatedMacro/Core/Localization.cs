using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace AutomatedMacro;

public enum AppLanguage { Hr, En, De }

/// <summary>
/// Prijevodi sucelja. XAML koristi {local:Tr Kljuc}, kod koristi Loc.T("Kljuc") / Loc.F("Kljuc", args).
/// Promjena jezika odmah osvjezava sve natpise (bez ponovnog pokretanja).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    /// <summary>Okida se nakon promjene jezika (za tekstove koji se racunaju u kodu).</summary>
    public static event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Language { get; private set; } = AppLanguage.Hr;

    public static CultureInfo Culture => Instance.Language switch
    {
        AppLanguage.En => CultureInfo.GetCultureInfo("en-US"),
        AppLanguage.De => CultureInfo.GetCultureInfo("de-DE"),
        _ => CultureInfo.GetCultureInfo("hr-HR"),
    };

    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);

    public static string F(string key, params object[] args) => string.Format(Culture, Instance.Get(key), args);

    public void SetLanguage(AppLanguage language)
    {
        if (Language == language) return;
        Language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }

    /// <summary>Jezik sustava: hrvatski (i bliski), njemacki, inace engleski.</summary>
    public static AppLanguage Detect() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "hr" or "bs" or "sr" => AppLanguage.Hr,
        "de" => AppLanguage.De,
        _ => AppLanguage.En,
    };

    /// <summary>"5 koraka" / "5 steps" / "5 Schritte".</summary>
    public static string Steps(int n) => Instance.Language switch
    {
        AppLanguage.En => n == 1 ? "1 step" : $"{n} steps",
        AppLanguage.De => n == 1 ? "1 Schritt" : $"{n} Schritte",
        _ => $"{n} {HrPlural(n, "korak", "koraka", "koraka")}",
    };

    private static string HrPlural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14)) return few;
        return many;
    }

    private string Get(string key)
    {
        if (!Strings.Table.TryGetValue(key, out var values)) return key;
        int i = (int)Language;
        return i < values.Length && !string.IsNullOrEmpty(values[i]) ? values[i] : values[(int)AppLanguage.En];
    }
}

/// <summary>{local:Tr Kljuc} — prevedeni tekst koji prati promjenu jezika.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
