using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AutomatedMacro;

public sealed class RunSettings : ObservableObject
{
    private bool _loop = true;
    private bool _infinite = true;
    private int _loopCount = 10;
    private int _cycleDelayMs = 1000;
    private int _startDelaySec = 3;

    /// <summary>Ponavljaj proceduru u petlji. Iskljuceno = izvedi jednom.</summary>
    public bool Loop { get => _loop; set { if (Set(ref _loop, value)) ChangeTracker.Notify(); } }

    /// <summary>Petlja se vrti dok je ne zaustavis (F10).</summary>
    public bool Infinite { get => _infinite; set { if (Set(ref _infinite, value)) ChangeTracker.Notify(); } }

    public int LoopCount { get => _loopCount; set { if (Set(ref _loopCount, Math.Clamp(value, 1, 1_000_000))) ChangeTracker.Notify(); } }

    /// <summary>Broj ciklusa za izvodjenje; 0 = beskonacno.</summary>
    [JsonIgnore] public int EffectiveCycles => !Loop ? 1 : Infinite ? 0 : LoopCount;

    private bool _gameClicks;
    private int _gameClickMs = 80;

    /// <summary>Klikovi za igre (Roblox i sl.): aktivacija prozora, pravi pomak misa, dulji pritisak.</summary>
    public bool GameClicks { get => _gameClicks; set { if (Set(ref _gameClicks, value)) ChangeTracker.Notify(); } }

    /// <summary>Koliko dugo mis stoji na gumbu prije klika i koliko se tipka drzi (ms).</summary>
    public int GameClickMs { get => _gameClickMs; set { if (Set(ref _gameClickMs, Math.Clamp(value, 20, 2000))) ChangeTracker.Notify(); } }

    /// <summary>Pauza izmedju dva ciklusa.</summary>
    public int CycleDelayMs { get => _cycleDelayMs; set { if (Set(ref _cycleDelayMs, Math.Clamp(value, 0, 86_400_000))) ChangeTracker.Notify(); } }

    /// <summary>Odbrojavanje prije pokretanja, da stignes prebaciti na ciljni prozor.</summary>
    public int StartDelaySec { get => _startDelaySec; set { if (Set(ref _startDelaySec, Math.Clamp(value, 0, 60))) ChangeTracker.Notify(); } }
}

public sealed class Procedure
{
    public string Format { get; set; } = "automated-macro";
    public int Version { get; set; } = 1;
    public RunSettings Settings { get; set; } = new();
    public GroupNode Root { get; set; } = new() { Name = "Nova procedura" };

    public static Procedure CreateNew()
    {
        var p = new Procedure();
        p.Root.IsRoot = true;
        return p;
    }
}

public static class ProcedureFile
{
    public const string Extension = ".macro";
    public static string DialogFilter => Loc.T("FileFilter");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Hrvatska slova ostaju citljiva u datoteci (č umjesto č).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Save(Procedure procedure, string path)
    {
        var json = JsonSerializer.Serialize(procedure, Options);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    public static Procedure Load(string path)
    {
        using var _ = ChangeTracker.Suspend();
        var procedure = JsonSerializer.Deserialize<Procedure>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException(Loc.T("Err_EmptyFile"));
        if (procedure.Format != "automated-macro")
            throw new InvalidDataException(Loc.T("Err_NotMacro"));
        procedure.Root ??= new GroupNode { Name = "Procedura" };
        procedure.Root.IsRoot = true;
        procedure.Settings ??= new RunSettings();
        return procedure;
    }

    /// <summary>Duboka kopija cvora (za dupliciranje).</summary>
    public static MacroNode Clone(MacroNode node)
    {
        using var _ = ChangeTracker.Suspend();
        var json = JsonSerializer.Serialize(node, Options);
        return JsonSerializer.Deserialize<MacroNode>(json, Options)!;
    }
}

public static class Palette
{
    public const string Default = "#7C5CFF";

    public static IReadOnlyList<string> Colors { get; } =
    [
        "#7C5CFF", "#F5A524", "#14B8A6", "#4CA6FF",
        "#E5484D", "#3DD68C", "#E879C6", "#F76B15",
        "#A78BFA", "#F5D90A", "#00A2C7", "#3E63DD",
        "#46A758", "#AD7F58", "#8B8D98", "#EDEEF0",
    ];

    private static readonly Regex Hex = new("^#?([0-9a-fA-F]{6})$", RegexOptions.Compiled);

    /// <summary>Vraca "#RRGGBB" ili null ako vrijednost nije valjana boja.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var m = Hex.Match(value.Trim());
        return m.Success ? "#" + m.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static string Next(int index) => Colors[(index % Colors.Count + Colors.Count) % Colors.Count];
}
