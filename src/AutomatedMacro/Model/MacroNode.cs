using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace AutomatedMacro;

public enum MouseButtonKind { Left, Right, Middle }
public enum TextMode { Fixed, Random }
public enum CharsetKind { LettersDigits, Letters, Lowercase, LowercaseDigits, Digits }

/// <summary>Cvor u stablu procedure: grupa ili korak.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GroupNode), "group")]
[JsonDerivedType(typeof(ClickStep), "click")]
[JsonDerivedType(typeof(DragStep), "drag")]
[JsonDerivedType(typeof(TextStep), "text")]
[JsonDerivedType(typeof(WaitStep), "wait")]
[JsonDerivedType(typeof(KeyStep), "key")]
public abstract class MacroNode : ObservableObject
{
    private string _name = "";
    private bool _enabled = true;
    private bool _isActive;
    private bool _isExpanded = true;
    private int _depth;
    private string _number = "";

    public string Name { get => _name; set => SetData(ref _name, value ?? ""); }

    /// <summary>Iskljuceni korak (ili grupa sa svim sadrzajem) se preskace pri izvodjenju.</summary>
    public bool Enabled { get => _enabled; set => SetData(ref _enabled, value); }

    [JsonIgnore] public GroupNode? Parent { get; internal set; }

    [JsonIgnore]
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (Set(ref _isExpanded, value)) ChangeTracker.NotifyStructure(); }
    }

    /// <summary>Korak koji se upravo izvodi (isticanje u tablici).</summary>
    [JsonIgnore] public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    /// <summary>Dubina u hijerarhiji (0 = izravno u proceduri).</summary>
    [JsonIgnore] public int Depth { get => _depth; set => Set(ref _depth, value); }

    /// <summary>Hijerarhijski broj, npr. "2.1".</summary>
    [JsonIgnore] public string Number { get => _number; set => Set(ref _number, value); }

    [JsonIgnore] public virtual bool IsGroup => false;
    /// <summary>Ima li smisla "pauza nakon koraka" (cekanje i grupe nemaju).</summary>
    [JsonIgnore] public virtual bool HasDelayAfter => false;
    [JsonIgnore] public abstract string Glyph { get; }
    [JsonIgnore] public abstract string KindLabel { get; }
    /// <summary>Kratki opis vrste koraka (zaglavlje svojstava).</summary>
    [JsonIgnore] public abstract string Description { get; }
    /// <summary>Dulje objasnjenje ("O ovom koraku").</summary>
    [JsonIgnore] public abstract string About { get; }
    [JsonIgnore] public abstract IReadOnlyList<string> Tips { get; }
    /// <summary>Boja ikone (hex).</summary>
    [JsonIgnore] public virtual string IconColor => "#D9D9E3";

    /// <summary>Stupac AKCIJA.</summary>
    [JsonIgnore] public abstract string ActionText { get; }
    /// <summary>Stupac PAUZA.</summary>
    [JsonIgnore] public abstract string DelayText { get; }
    /// <summary>Drugi red ispod naziva (npr. tekst koji se upisuje).</summary>
    [JsonIgnore] public virtual string DetailText => "";
    [JsonIgnore] public virtual string DetailColor => "#B4A5FF";
    /// <summary>Tocka na ekranu koju korak koristi (za pregled).</summary>
    [JsonIgnore] public virtual (int X, int Y)? Point => null;
    [JsonIgnore] public virtual (int X, int Y)? Point2 => null;

    protected bool SetData<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name)) return false;
        RaiseDisplay();
        ChangeTracker.Notify();
        return true;
    }

    protected void RaiseDisplay()
    {
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(DelayText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(DetailColor));
        OnPropertyChanged(nameof(Point));
    }

    public bool IsDescendantOf(GroupNode group)
    {
        for (var p = Parent; p != null; p = p.Parent)
            if (ReferenceEquals(p, group)) return true;
        return false;
    }

    public IEnumerable<GroupNode> Ancestors()
    {
        for (var p = Parent; p != null; p = p.Parent) yield return p;
    }

    /// <summary>Nakon promjene jezika: osvjezi sve prikazane tekstove ovog cvora.</summary>
    public void RefreshLocalized() => OnPropertyChanged(string.Empty);

    public static string FormatMs(int ms) => ms >= 1000 && ms % 100 == 0
        ? (ms / 1000.0).ToString("0.#", Loc.Culture) + " s"
        : $"{ms} ms";
}

/// <summary>Grupa koraka. Sluzi za vizualno grupiranje; izvodi se sve sto je unutra, redom.</summary>
public sealed class GroupNode : MacroNode
{
    private string _color = Palette.Default;
    private ObservableCollection<MacroNode> _children = new();

    public GroupNode()
    {
        _children.CollectionChanged += OnChildrenChanged;
    }

    /// <summary>Boja ikone grupe (#RRGGBB). Cisto kozmeticki.</summary>
    public string Color
    {
        get => _color;
        set
        {
            var normalized = Palette.Normalize(value);
            if (normalized == null) { OnPropertyChanged(); return; }
            if (SetData(ref _color, normalized)) OnPropertyChanged(nameof(IconColor));
        }
    }

    public ObservableCollection<MacroNode> Children
    {
        get => _children;
        set
        {
            _children.CollectionChanged -= OnChildrenChanged;
            _children = value ?? new();
            _children.CollectionChanged += OnChildrenChanged;
            foreach (var c in _children) c.Parent = this;
            OnPropertyChanged();
            NotifyCounts();
        }
    }

    /// <summary>Korijen procedure (ne prikazuje se kao red u tablici).</summary>
    [JsonIgnore] public bool IsRoot { get; set; }

    [JsonIgnore] public override bool IsGroup => true;
    [JsonIgnore] public override string Glyph => "";
    [JsonIgnore] public override string KindLabel => Loc.T(IsRoot ? "Kind_Procedure" : "Group");
    [JsonIgnore] public override string Description => Loc.T("Desc_Group");
    [JsonIgnore] public override string About => Loc.T("About_Group");
    [JsonIgnore] public override IReadOnlyList<string> Tips => [Loc.T("Tips_Group1"), Loc.T("Tips_Group2"), Loc.T("Tips_Group3")];
    [JsonIgnore] public override string IconColor => Color;
    [JsonIgnore] public int StepCount => AllSteps().Count();
    [JsonIgnore] public override string ActionText => Loc.F("GroupAction", Loc.Steps(StepCount));
    [JsonIgnore] public override string DelayText => "—";

    public IEnumerable<StepNode> AllSteps(bool onlyEnabled = false)
    {
        foreach (var c in _children)
        {
            if (onlyEnabled && !c.Enabled) continue;
            if (c is StepNode s) yield return s;
            else if (c is GroupNode g)
                foreach (var inner in g.AllSteps(onlyEnabled)) yield return inner;
        }
    }

    public IEnumerable<GroupNode> AllGroups()
    {
        foreach (var c in _children.OfType<GroupNode>())
        {
            yield return c;
            foreach (var inner in c.AllGroups()) yield return inner;
        }
    }

    public IEnumerable<MacroNode> AllNodes()
    {
        foreach (var c in _children)
        {
            yield return c;
            if (c is GroupNode g)
                foreach (var inner in g.AllNodes()) yield return inner;
        }
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (MacroNode n in e.NewItems) n.Parent = this;
        NotifyCounts();
        ChangeTracker.Notify();
        ChangeTracker.NotifyStructure();
    }

    private void NotifyCounts()
    {
        for (GroupNode? g = this; g != null; g = g.Parent)
        {
            g.OnPropertyChanged(nameof(StepCount));
            g.OnPropertyChanged(nameof(ActionText));
        }
    }
}

public abstract class StepNode : MacroNode
{
    private int _delayAfterMs = 200;

    /// <summary>Pauza nakon ovog koraka.</summary>
    public int DelayAfterMs { get => _delayAfterMs; set => SetData(ref _delayAfterMs, Math.Clamp(value, 0, 86_400_000)); }

    [JsonIgnore] public override string DelayText => FormatMs(DelayAfterMs);
    [JsonIgnore] public override bool HasDelayAfter => true;

    /// <summary>"Lijevi klik", "Desni dvoklik"... na odabranom jeziku.</summary>
    public static string ClickLabel(MouseButtonKind b, int clicks) => Loc.T($"Click_{b}_{(clicks == 2 ? 2 : 1)}");
}

public sealed class ClickStep : StepNode
{
    private int _x, _y, _clicks = 1;
    private MouseButtonKind _button;

    public int X { get => _x; set => SetData(ref _x, value); }
    public int Y { get => _y; set => SetData(ref _y, value); }
    public MouseButtonKind Button { get => _button; set => SetData(ref _button, value); }
    /// <summary>1 = klik, 2 = dvoklik.</summary>
    public int Clicks { get => _clicks; set => SetData(ref _clicks, Math.Clamp(value, 1, 2)); }

    [JsonIgnore] public override string Glyph => "";
    [JsonIgnore] public override string KindLabel => Loc.T("Click");
    [JsonIgnore] public override string Description => Loc.T("Desc_Click");
    [JsonIgnore] public override string About => Loc.T("About_Click");
    [JsonIgnore] public override IReadOnlyList<string> Tips => [Loc.T("Tips_Click1"), Loc.T("Tips_Delay")];
    [JsonIgnore] public override string ActionText => $"{ClickLabel(Button, Clicks)} ({X}, {Y})";
    [JsonIgnore] public override (int X, int Y)? Point => (X, Y);
}

public sealed class DragStep : StepNode
{
    private int _x, _y, _x2, _y2, _durationMs = 300;
    private MouseButtonKind _button;

    public int X { get => _x; set => SetData(ref _x, value); }
    public int Y { get => _y; set => SetData(ref _y, value); }
    public int X2 { get => _x2; set => SetData(ref _x2, value); }
    public int Y2 { get => _y2; set => SetData(ref _y2, value); }
    public MouseButtonKind Button { get => _button; set => SetData(ref _button, value); }
    public int DurationMs { get => _durationMs; set => SetData(ref _durationMs, Math.Clamp(value, 0, 60000)); }

    [JsonIgnore] public override string Glyph => "";
    [JsonIgnore] public override string KindLabel => Loc.T("Kind_Drag");
    [JsonIgnore] public override string Description => Loc.T("Desc_Drag");
    [JsonIgnore] public override string About => Loc.T("About_Drag");
    [JsonIgnore] public override IReadOnlyList<string> Tips => [Loc.T("Tips_Drag1"), Loc.T("Tips_Drag2")];
    [JsonIgnore] public override string ActionText => Loc.F("DragAction", X, Y, X2, Y2);
    [JsonIgnore] public override (int X, int Y)? Point => (X, Y);
    [JsonIgnore] public override (int X, int Y)? Point2 => (X2, Y2);
}

/// <summary>Klik na polje (opcionalno) pa unos fiksnog ili nasumicnog jedinstvenog teksta.</summary>
public sealed class TextStep : StepNode
{
    public const int MinAllowed = 3;
    public const int MaxAllowed = 40;

    private bool _clickFirst = true;
    private int _x, _y;
    private TextMode _mode = TextMode.Fixed;
    private string _text = "";
    private bool _masked;
    private int _minLength = 8, _maxLength = 12;
    private CharsetKind _charset = CharsetKind.LettersDigits;
    private string _prefix = "", _suffix = "";
    private bool _clearFirst, _pressEnter;
    private int _charDelayMs = 15;

    public bool ClickFirst { get => _clickFirst; set => SetData(ref _clickFirst, value); }
    public int X { get => _x; set => SetData(ref _x, value); }
    public int Y { get => _y; set => SetData(ref _y, value); }
    public TextMode Mode { get => _mode; set => SetData(ref _mode, value); }
    public string Text { get => _text; set => SetData(ref _text, value ?? ""); }
    /// <summary>U tablici prikazi tocke umjesto teksta (npr. lozinka).</summary>
    public bool Masked { get => _masked; set => SetData(ref _masked, value); }
    public int MinLength { get => _minLength; set => SetData(ref _minLength, Math.Clamp(value, MinAllowed, MaxAllowed)); }
    public int MaxLength { get => _maxLength; set => SetData(ref _maxLength, Math.Clamp(value, MinAllowed, MaxAllowed)); }
    public CharsetKind Charset { get => _charset; set => SetData(ref _charset, value); }
    /// <summary>Fiksni tekst ispred nasumicnog dijela (npr. "user_").</summary>
    public string Prefix { get => _prefix; set => SetData(ref _prefix, value ?? ""); }
    /// <summary>Fiksni tekst iza nasumicnog dijela (npr. "@mail.com").</summary>
    public string Suffix { get => _suffix; set => SetData(ref _suffix, value ?? ""); }
    /// <summary>Prije unosa oznaci sve u polju (Ctrl+A) i obrisi.</summary>
    public bool ClearFirst { get => _clearFirst; set => SetData(ref _clearFirst, value); }
    public bool PressEnter { get => _pressEnter; set => SetData(ref _pressEnter, value); }
    public int CharDelayMs { get => _charDelayMs; set => SetData(ref _charDelayMs, Math.Clamp(value, 0, 2000)); }

    [JsonIgnore] public int LowLength => Math.Min(MinLength, MaxLength);
    [JsonIgnore] public int HighLength => Math.Max(MinLength, MaxLength);

    [JsonIgnore] public override string Glyph => "";
    [JsonIgnore] public override string KindLabel => Loc.T("Kind_Text");
    [JsonIgnore] public override string Description => Loc.T("Desc_Text");
    [JsonIgnore] public override string About => Loc.T("About_Text");
    [JsonIgnore] public override IReadOnlyList<string> Tips => [Loc.T("Tips_Text1"), Loc.T("Tips_Text2"), Loc.T("Tips_Delay")];

    [JsonIgnore]
    public override string ActionText
    {
        get
        {
            string what = Mode == TextMode.Fixed ? Loc.T("FixedText") : Loc.F("RandomTextAction", RangeText);
            return ClickFirst ? $"{what} · ({X}, {Y})" : what;
        }
    }

    [JsonIgnore]
    public override string DetailText => Mode == TextMode.Fixed
        ? Loc.F("Detail_Text", Masked ? new string('•', Math.Clamp(Text.Length, 6, 14)) : Shorten(Text.Replace("\r", "").Replace("\n", " ⏎ "), 48))
          + (PressEnter ? "  + Enter" : "")
        : Loc.F("Detail_Random", RangeText)
          + (Prefix.Length + Suffix.Length > 0 ? $"  {Prefix}…{Suffix}" : "")
          + (PressEnter ? "  + Enter" : "");

    [JsonIgnore] public override string DetailColor => Mode == TextMode.Fixed ? "#B4A5FF" : "#F5A524";
    [JsonIgnore] public override (int X, int Y)? Point => ClickFirst ? (X, Y) : null;

    private string RangeText => LowLength == HighLength ? $"{LowLength}" : $"{LowLength}–{HighLength}";

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

public sealed class WaitStep : StepNode
{
    private int _ms = 1000, _maxMs = 2000;
    private bool _useRandom;

    public WaitStep() => DelayAfterMs = 0;

    public int Ms { get => _ms; set => SetData(ref _ms, Math.Clamp(value, 0, 86_400_000)); }
    /// <summary>Ako je ukljuceno, ceka nasumicno izmedju Ms i MaxMs.</summary>
    public bool UseRandom { get => _useRandom; set => SetData(ref _useRandom, value); }
    public int MaxMs { get => _maxMs; set => SetData(ref _maxMs, Math.Clamp(value, 0, 86_400_000)); }

    [JsonIgnore] public override string Glyph => "";
    [JsonIgnore] public override string KindLabel => Loc.T("Wait");
    [JsonIgnore] public override string Description => Loc.T("Desc_Wait");
    [JsonIgnore] public override string About => Loc.T("About_Wait");
    [JsonIgnore] public override IReadOnlyList<string> Tips => [Loc.T("Tips_Wait1"), Loc.T("Tips_Wait2")];
    [JsonIgnore] public override string ActionText => Loc.T(UseRandom ? "RandomWait" : "Wait");
    [JsonIgnore] public override bool HasDelayAfter => false;
    [JsonIgnore]
    public override string DelayText => UseRandom
        ? $"{FormatMs(Math.Min(Ms, MaxMs))}–{FormatMs(Math.Max(Ms, MaxMs))}"
        : FormatMs(Ms);
}

public sealed class KeyStep : StepNode
{
    private int _vk = 0x0D;
    private bool _ctrl, _shift, _alt, _win;
    private int _repeat = 1;

    /// <summary>Windows virtual-key kod.</summary>
    public int Vk { get => _vk; set { if (SetData(ref _vk, value)) OnPropertyChanged(nameof(Combo)); } }
    public bool Ctrl { get => _ctrl; set { if (SetData(ref _ctrl, value)) OnPropertyChanged(nameof(Combo)); } }
    public bool Shift { get => _shift; set { if (SetData(ref _shift, value)) OnPropertyChanged(nameof(Combo)); } }
    public bool Alt { get => _alt; set { if (SetData(ref _alt, value)) OnPropertyChanged(nameof(Combo)); } }
    public bool Win { get => _win; set { if (SetData(ref _win, value)) OnPropertyChanged(nameof(Combo)); } }
    public int Repeat { get => _repeat; set => SetData(ref _repeat, Math.Clamp(value, 1, 1000)); }

    [JsonIgnore] public override string Glyph => "";
    [JsonIgnore] public override string KindLabel => Loc.T("Key");
    [JsonIgnore] public override string Description => Loc.T("Desc_Key");
    [JsonIgnore] public override string About => Loc.T("About_Key");
    [JsonIgnore] public override IReadOnlyList<string> Tips => [Loc.T("Tips_Key1"), Loc.T("Tips_Key2")];
    [JsonIgnore] public string Combo => KeyNames.Combo(Vk, Ctrl, Shift, Alt, Win);
    [JsonIgnore] public override string ActionText => Loc.F("KeyAction", Combo) + (Repeat > 1 ? $" ×{Repeat}" : "");

    public void SetCombo(int vk, bool ctrl, bool shift, bool alt, bool win)
    {
        Vk = vk; Ctrl = ctrl; Shift = shift; Alt = alt; Win = win;
    }
}

public static class KeyNames
{
    public static string Combo(int vk, bool ctrl, bool shift, bool alt, bool win)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        if (win) parts.Add("Win");
        parts.Add(Name(vk));
        return string.Join("+", parts);
    }

    public static string Name(int vk)
    {
        var key = KeyInterop.KeyFromVirtualKey(vk);
        return key switch
        {
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Space => "Space",
            Key.Next => "PageDown",
            Key.Prior => "PageUp",
            Key.Capital => "CapsLock",
            Key.Snapshot => "PrintScreen",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Left => "←",
            Key.Right => "→",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
            Key.None => $"VK 0x{vk:X2}",
            _ => key.ToString(),
        };
    }
}
