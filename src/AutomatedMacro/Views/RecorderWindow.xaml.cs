using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace AutomatedMacro;

public sealed record TargetOption(GroupNode Group, string Label);

/// <summary>Plutajuci panel tijekom snimanja: prikazuje zadnji klik i sprema ga kao korak.</summary>
public partial class RecorderWindow : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _vm;
    private readonly Stack<MacroNode> _added = new();
    private readonly DispatcherTimer _cursorTimer;
    private readonly DispatcherTimer _messageTimer;
    private bool _closingSilently;

    private CapturedAction? _capture;
    private bool _isTextStep;
    private TextMode _textKind = TextMode.Fixed;
    private string _fixedText = "";
    private bool _masked;
    private int _minLength = 8, _maxLength = 12;
    private CharsetKind _charset = CharsetKind.LettersDigits;
    private string _prefix = "", _suffix = "";
    private bool _clearFirst, _pressEnter;
    private int _delayAfterMs = 200;
    private int _waitMs = 1000;
    private KeyPreset? _selectedKey = Choices.Keys[0];
    private string _groupName = "";
    private TargetOption? _selectedTarget;
    private string _cursorText = "";
    private string _message = "";
    private string _lastSavedText = Loc.T("NothingYet");

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Korisnik je zatvorio panel ili kliknuo "Zavrsi".</summary>
    public event Action? Finished;

    public RecorderWindow(MainViewModel vm, GroupNode target)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = this;
        RefreshTargets(target);

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _cursorTimer.Tick += (_, _) =>
        {
            var (x, y) = Win32.CursorPosition();
            CursorText = $"{x}, {y}";
        };
        _cursorTimer.Start();

        _messageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _messageTimer.Tick += (_, _) => { Message = ""; _messageTimer.Stop(); };

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16;
            Top = area.Top + 16;
        };
    }

    // ---------------------------------------------------------------- svojstva za prikaz

    public ObservableCollection<TargetOption> Targets { get; } = new();
    public TargetOption? SelectedTarget { get => _selectedTarget; set => Set(ref _selectedTarget, value); }

    public bool HasCapture => _capture != null;
    public string CaptureText => _capture?.Describe() ?? Loc.T("Capture_None");

    public bool IsTextStep
    {
        get => _isTextStep;
        set { if (Set(ref _isTextStep, value)) OnPropertyChanged(nameof(IsClickStep)); }
    }

    public bool IsClickStep
    {
        get => !_isTextStep;
        set { if (value) IsTextStep = false; }
    }

    public TextMode TextKind { get => _textKind; set => Set(ref _textKind, value); }
    public string FixedText { get => _fixedText; set => Set(ref _fixedText, value); }
    public bool Masked { get => _masked; set => Set(ref _masked, value); }
    public int MinLength { get => _minLength; set => Set(ref _minLength, Math.Clamp(value, TextStep.MinAllowed, TextStep.MaxAllowed)); }
    public int MaxLength { get => _maxLength; set => Set(ref _maxLength, Math.Clamp(value, TextStep.MinAllowed, TextStep.MaxAllowed)); }
    public CharsetKind Charset { get => _charset; set => Set(ref _charset, value); }
    public string Prefix { get => _prefix; set => Set(ref _prefix, value); }
    public string Suffix { get => _suffix; set => Set(ref _suffix, value); }
    public bool ClearFirst { get => _clearFirst; set => Set(ref _clearFirst, value); }
    public bool PressEnter { get => _pressEnter; set => Set(ref _pressEnter, value); }
    public int DelayAfterMs { get => _delayAfterMs; set => Set(ref _delayAfterMs, Math.Max(0, value)); }
    public int WaitMs { get => _waitMs; set => Set(ref _waitMs, Math.Max(0, value)); }
    public KeyPreset? SelectedKey { get => _selectedKey; set => Set(ref _selectedKey, value); }
    public string GroupName { get => _groupName; set => Set(ref _groupName, value); }
    public string CursorText { get => _cursorText; set => Set(ref _cursorText, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
    public string LastSavedText { get => _lastSavedText; set => Set(ref _lastSavedText, value); }
    public int SavedCount => _added.Count;
    public bool CanUndo => _added.Count > 0;

    // ---------------------------------------------------------------- radnje

    public void SetCapture(CapturedAction action)
    {
        _capture = action;
        OnPropertyChanged(nameof(HasCapture));
        OnPropertyChanged(nameof(CaptureText));
        Message = "";
    }

    /// <summary>Spremi zadnji klik kao korak (gumb ili F8).</summary>
    public void SaveStep()
    {
        if (_capture is not { } c)
        {
            Flash(Loc.T("Warn_ClickFirst"));
            return;
        }

        StepNode node;
        if (IsTextStep)
        {
            node = new TextStep
            {
                Name = Loc.T("Kind_Text"),
                ClickFirst = true,
                X = c.X,
                Y = c.Y,
                Mode = TextKind,
                Text = FixedText,
                Masked = Masked,
                MinLength = MinLength,
                MaxLength = MaxLength,
                Charset = Charset,
                Prefix = Prefix,
                Suffix = Suffix,
                ClearFirst = ClearFirst,
                PressEnter = PressEnter,
            };
        }
        else if (c.Kind == CaptureKind.Drag)
        {
            node = new DragStep { Name = Loc.T("Kind_Drag"), X = c.X, Y = c.Y, X2 = c.X2, Y2 = c.Y2, Button = c.Button };
        }
        else
        {
            node = new ClickStep { Name = Loc.T(c.Clicks == 2 ? "DoubleClick" : "Click"), X = c.X, Y = c.Y, Button = c.Button, Clicks = c.Clicks };
        }
        node.DelayAfterMs = DelayAfterMs;

        Add(node);
        _capture = null;
        OnPropertyChanged(nameof(HasCapture));
        OnPropertyChanged(nameof(CaptureText));
        IsTextStep = false;
    }

    private void Add(MacroNode node)
    {
        var target = CurrentTarget();
        target.Children.Add(node);
        target.IsExpanded = true;
        _added.Push(node);
        OnPropertyChanged(nameof(SavedCount));
        OnPropertyChanged(nameof(CanUndo));
        LastSavedText = $"{node.KindLabel}: {node.ActionText}";
        _vm.RefreshRows();
        _vm.Select(node);
        _vm.AddLog(Loc.F("Log_Recorded", node.Name, node.ActionText));
    }

    private GroupNode CurrentTarget()
    {
        var g = SelectedTarget?.Group;
        // Grupa je mozda u medjuvremenu uklonjena (npr. "Ponisti zadnji").
        return g != null && (g.IsRoot || g.IsDescendantOf(_vm.Root)) ? g : _vm.Root;
    }

    private void Flash(string text)
    {
        Message = text;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    private void RefreshTargets(GroupNode? select = null)
    {
        select ??= SelectedTarget?.Group;
        Targets.Clear();
        Targets.Add(new TargetOption(_vm.Root, Loc.T("TargetRoot")));
        foreach (var g in _vm.Root.AllGroups())
        {
            int depth = g.Ancestors().Count(a => !a.IsRoot);
            Targets.Add(new TargetOption(g, new string(' ', depth * 4) + "▸ " + g.Name));
        }
        SelectedTarget = Targets.FirstOrDefault(t => ReferenceEquals(t.Group, select)) ?? Targets[0];
    }

    private void SaveStep_Click(object sender, RoutedEventArgs e) => SaveStep();

    private void AddWait_Click(object sender, RoutedEventArgs e) =>
        Add(new WaitStep { Name = Loc.T("Wait"), Ms = WaitMs });

    private void AddKey_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedKey is not { } k) return;
        var step = k.ToStep();
        step.DelayAfterMs = DelayAfterMs;
        Add(step);
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = _vm.CreateGroup(GroupName);
        Add(group);
        GroupName = "";
        RefreshTargets(group);
    }

    private void ExitGroup_Click(object sender, RoutedEventArgs e)
    {
        var parent = CurrentTarget().Parent;
        if (parent != null) RefreshTargets(parent);
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_added.Count == 0) return;
        var node = _added.Pop();
        node.Parent?.Children.Remove(node);
        OnPropertyChanged(nameof(SavedCount));
        OnPropertyChanged(nameof(CanUndo));
        LastSavedText = _added.Count > 0 ? $"{_added.Peek().KindLabel}: {_added.Peek().ActionText}" : Loc.T("NothingYet");
        if (node is GroupNode) RefreshTargets();
        _vm.RefreshRows();
        _vm.AddLog(Loc.F("Log_Undone", node.Name));
    }

    private void Targets_DropDownOpened(object? sender, EventArgs e) => RefreshTargets();

    private void Finish_Click(object sender, RoutedEventArgs e) => Finished?.Invoke();

    /// <summary>Zatvaranje iz glavnog prozora (bez ponovnog okidanja Finished).</summary>
    public void CloseSilently()
    {
        _closingSilently = true;
        _cursorTimer.Stop();
        _messageTimer.Stop();
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingSilently) return;
        e.Cancel = true;
        Finished?.Invoke();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
