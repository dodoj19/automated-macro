using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutomatedMacro;

/// <summary>Postavke programa (ne procedure): zadnja datoteka, mapa s makroima, ponasanje prozora.</summary>
public sealed class AppSettings : ObservableObject
{
    private bool _minimizeDuringRun = true;
    private bool _minimizeDuringRecord = true;

    private string? _libraryFolder;
    private AppLanguage? _language;

    public string? LastFile { get; set; }

    /// <summary>Jezik sucelja; null = prema jeziku sustava (postavlja se pri pokretanju).</summary>
    public AppLanguage? Language { get => _language; set => Set(ref _language, value); }

    public string? LibraryFolder
    {
        get => _libraryFolder;
        set { if (Set(ref _libraryFolder, value)) OnPropertyChanged(nameof(EffectiveLibraryFolder)); }
    }

    public bool MinimizeDuringRun { get => _minimizeDuringRun; set => Set(ref _minimizeDuringRun, value); }
    public bool MinimizeDuringRecord { get => _minimizeDuringRecord; set => Set(ref _minimizeDuringRecord, value); }

    [JsonIgnore]
    public string EffectiveLibraryFolder =>
        string.IsNullOrWhiteSpace(LibraryFolder) ? MacroLibrary.DefaultRoot : LibraryFolder;

    // Prijenosni nacin: postavke stoje pokraj exe datoteke (zajedno s mapom Makroi).
    // Ako se tamo ne moze pisati (npr. Program Files), koristi se %AppData%.
    private static string PortablePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static string RoamingPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutomatedMacro", "settings.json");

    public static AppSettings Load()
    {
        foreach (var path in new[] { PortablePath, RoamingPath })
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
            }
            catch { /* ostecene postavke -> sljedeca lokacija ili zadane */ }
        }
        return new();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        foreach (var path in new[] { PortablePath, RoamingPath })
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
                return;
            }
            catch { /* pokusaj sljedecu lokaciju */ }
        }
    }
}
