using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace AutomatedMacro;

public enum AppMode { Idle, Recording, Running, Picking }
public enum DropPosition { Before, After, Into }

public sealed record LogEntry(string Time, string Text);

public sealed class MainViewModel : ObservableObject
{
    private Procedure _procedure = Procedure.CreateNew();
    private MacroNode? _selected;
    private string? _filePath;
    private bool _isDirty;
    private AppMode _mode = AppMode.Idle;
    private string _runInfo = "";
    private string _exampleText = "";
    private string _librarySearch = "";
    private int _executedLoops;
    private bool _refreshQueued;
    private List<MacroNode>? _clipboard;

    public MainViewModel(AppSettings app)
    {
        App = app;
        ChangeTracker.Changed += () =>
        {
            IsDirty = true;
            QueueRefresh();
        };
        ChangeTracker.StructureChanged += QueueRefresh;
        Loc.LanguageChanged += OnLanguageChanged;
        app.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AppSettings.Language) || app.Language is not { } language) return;
            Loc.Instance.SetLanguage(language);
            app.Save();
        };
        RefreshRows();
        RefreshLibrary();
    }

    public AppSettings App { get; }
    public string Version => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Vidljivi redovi tablice (stablo "razmotano" prema prosirenim grupama).</summary>
    public ObservableCollection<MacroNode> Rows { get; } = new();
    public ObservableCollection<LogEntry> Log { get; } = new();
    public ObservableCollection<LibraryItem> Library { get; } = new();

    public Procedure Procedure => _procedure;
    public RunSettings Settings => _procedure.Settings;
    public GroupNode Root => _procedure.Root;

    public MacroNode? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            ExampleText = "";
        }
    }

    public bool HasSelection => _selected != null;

    public string ExampleText { get => _exampleText; set => Set(ref _exampleText, value); }

    public string? FilePath
    {
        get => _filePath;
        private set
        {
            if (!Set(ref _filePath, value)) return;
            OnPropertyChanged(nameof(DocumentName));
            OnPropertyChanged(nameof(WindowTitle));
            MarkCurrentInLibrary();
        }
    }

    public string DocumentName => FilePath == null ? Loc.T("NewProcedure") : Path.GetFileNameWithoutExtension(FilePath);

    public bool IsDirty
    {
        get => _isDirty;
        set { if (Set(ref _isDirty, value)) OnPropertyChanged(nameof(WindowTitle)); }
    }

    public string WindowTitle => $"{(IsDirty ? "● " : "")}{DocumentName} — Automated Macro";

    public AppMode Mode
    {
        get => _mode;
        set
        {
            if (!Set(ref _mode, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(ModeLabel));
        }
    }

    public bool IsIdle => _mode == AppMode.Idle;
    public bool IsRunning => _mode == AppMode.Running;
    public bool IsRecording => _mode == AppMode.Recording;
    public bool CanStop => _mode is AppMode.Running or AppMode.Recording;

    public string ModeLabel => _mode switch
    {
        AppMode.Running => Loc.T("Mode_Running"),
        AppMode.Recording => Loc.T("Mode_Recording"),
        AppMode.Picking => Loc.T("Mode_Picking"),
        _ => Loc.T("Mode_Idle"),
    };

    public string RunInfo { get => _runInfo; set => Set(ref _runInfo, value); }

    // ---------------------------------------------------------------- statistika

    public int TotalSteps => Root.AllSteps().Count();
    public int TotalGroups => Root.AllGroups().Count();
    public string EstimatedTime
    {
        get
        {
            var t = MacroRunner.EstimateCycle(Root);
            return $"~ {(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        }
    }

    public int ExecutedLoops { get => _executedLoops; set => Set(ref _executedLoops, value); }

    // ---------------------------------------------------------------- tablica

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.DataBind, () =>
        {
            _refreshQueued = false;
            RefreshRows();
        });
    }

    /// <summary>Uskladi Rows s trenutnim stablom, uz ponovno koristenje postojecih redova.</summary>
    public void RefreshRows()
    {
        var visible = new List<MacroNode>();
        Walk(Root, 0, "", visible, shown: true);

        for (int i = 0; i < visible.Count; i++)
        {
            var node = visible[i];
            if (i < Rows.Count && ReferenceEquals(Rows[i], node)) continue;
            int existing = IndexOf(Rows, node, i + 1);
            if (existing >= 0) Rows.Move(existing, i);
            else Rows.Insert(i, node);
        }
        while (Rows.Count > visible.Count) Rows.RemoveAt(Rows.Count - 1);

        OnPropertyChanged(nameof(TotalSteps));
        OnPropertyChanged(nameof(TotalGroups));
        OnPropertyChanged(nameof(EstimatedTime));
    }

    /// <summary>Numerira sve cvorove (1, 2, 2.1...) i skuplja one vidljive u tablici.</summary>
    private static void Walk(GroupNode group, int depth, string prefix, List<MacroNode> visible, bool shown)
    {
        int n = 0;
        foreach (var child in group.Children)
        {
            n++;
            child.Depth = depth;
            child.Number = prefix + n;
            if (shown) visible.Add(child);
            if (child is GroupNode g) Walk(g, depth + 1, child.Number + ".", visible, shown && g.IsExpanded);
        }
    }

    private static int IndexOf(IList<MacroNode> list, MacroNode node, int start)
    {
        for (int i = start; i < list.Count; i++)
            if (ReferenceEquals(list[i], node)) return i;
        return -1;
    }

    // ---------------------------------------------------------------- datoteke

    public void NewProcedure() => LoadProcedure(Procedure.CreateNew(), null);

    public void OpenFile(string path)
    {
        LoadProcedure(ProcedureFile.Load(path), path);
        AddLog(Loc.F("Log_Loaded", Path.GetFileNameWithoutExtension(path)));
    }

    public void SaveFile(string path)
    {
        Root.Name = Path.GetFileNameWithoutExtension(path);
        ProcedureFile.Save(_procedure, path);
        FilePath = path;
        IsDirty = false;
        AddLog(Loc.F("Log_Saved", Path.GetFileNameWithoutExtension(path)));
        RefreshLibrary();
    }

    /// <summary>Datoteka je preimenovana/premjestena izvana (npr. iz popisa makroa).</summary>
    public void FileMoved(string newPath)
    {
        FilePath = newPath;
        OnPropertyChanged(nameof(DocumentName));
    }

    private void LoadProcedure(Procedure p, string? path)
    {
        _procedure = p;
        Selected = null;
        ExecutedLoops = 0;
        OnPropertyChanged(nameof(Procedure));
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(Root));
        RefreshRows();
        FilePath = path;
        IsDirty = false;
        OnPropertyChanged(nameof(DocumentName));
        OnPropertyChanged(nameof(WindowTitle));
    }

    // ---------------------------------------------------------------- popis makroa

    public string LibrarySearch
    {
        get => _librarySearch;
        set { if (Set(ref _librarySearch, value)) RefreshLibrary(); }
    }

    public void RefreshLibrary()
    {
        Library.Clear();
        try
        {
            foreach (var item in MacroLibrary.Build(App.EffectiveLibraryFolder, _librarySearch)) Library.Add(item);
        }
        catch (Exception ex)
        {
            AddLog(Loc.F("Log_LibraryError", ex.Message));
        }
        MarkCurrentInLibrary();
    }

    private void MarkCurrentInLibrary()
    {
        foreach (var item in Library.SelectMany(i => i.Descendants()))
            item.IsCurrent = item.IsFile && FilePath != null
                && string.Equals(Path.GetFullPath(item.FullPath), Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- uredjivanje

    public void Select(MacroNode? node)
    {
        if (node != null)
        {
            bool expanded = false;
            foreach (var a in node.Ancestors().Where(a => !a.IsRoot && !a.IsExpanded))
            {
                a.IsExpanded = true;
                expanded = true;
            }
            if (expanded) RefreshRows();
        }
        Selected = node;
    }

    /// <summary>Novi cvor ide u odabranu grupu ili odmah iza odabranog koraka.</summary>
    public (GroupNode Parent, int Index) InsertionPoint() => _selected switch
    {
        GroupNode g => (g, g.Children.Count),
        StepNode s when s.Parent != null => (s.Parent, s.Parent.Children.IndexOf(s) + 1),
        _ => (Root, Root.Children.Count),
    };

    public void Insert(MacroNode node)
    {
        var (parent, index) = InsertionPoint();
        parent.Children.Insert(index, node);
        parent.IsExpanded = true;
        RefreshRows();
        Select(node);
    }

    public GroupNode CreateGroup(string? name = null)
    {
        int n = Root.AllGroups().Count();
        return new GroupNode
        {
            Name = string.IsNullOrWhiteSpace(name) ? Loc.F("GroupN", n + 1) : name.Trim(),
            Color = Palette.Next(n),
        };
    }

    public void DeleteSelected()
    {
        if (_selected?.Parent is not { } parent) return;
        int row = Rows.IndexOf(_selected);
        parent.Children.Remove(_selected);
        Selected = null;
        RefreshRows();
        if (Rows.Count > 0) Select(Rows[Math.Clamp(row, 0, Rows.Count - 1)]);
    }

    public void DuplicateSelected()
    {
        if (_selected?.Parent is not { } parent) return;
        var copy = ProcedureFile.Clone(_selected);
        parent.Children.Insert(parent.Children.IndexOf(_selected) + 1, copy);
        RefreshRows();
        Select(copy);
    }

    public void CopySelected()
    {
        if (_selected == null) return;
        _clipboard = [ProcedureFile.Clone(_selected)];
    }

    public void CutSelected()
    {
        if (_selected == null) return;
        CopySelected();
        DeleteSelected();
    }

    public bool CanPaste => _clipboard is { Count: > 0 };

    public void Paste()
    {
        if (_clipboard == null) return;
        MacroNode? last = null;
        foreach (var n in _clipboard)
        {
            last = ProcedureFile.Clone(n);
            Insert(last);
        }
        if (last != null) Select(last);
    }

    public void ToggleEnabledSelected()
    {
        if (_selected != null) _selected.Enabled = !_selected.Enabled;
    }

    public void SetAllExpanded(bool expanded)
    {
        using (ChangeTracker.Suspend())
            foreach (var g in Root.AllGroups()) g.IsExpanded = expanded;
        RefreshRows();
        if (_selected != null && !Rows.Contains(_selected)) Selected = null;
    }

    /// <summary>Gore: zamjena s prethodnim; na vrhu grupe izlazi iznad grupe.</summary>
    public void MoveSelectedUp()
    {
        if (_selected?.Parent is not { } parent) return;
        var node = _selected;
        int i = parent.Children.IndexOf(node);
        if (i > 0) parent.Children.Move(i, i - 1);
        else if (parent.Parent is { } grand)
        {
            parent.Children.RemoveAt(i);
            grand.Children.Insert(grand.Children.IndexOf(parent), node);
        }
        RefreshRows();
        Select(node);
    }

    /// <summary>Dolje: zamjena sa sljedecim; na dnu grupe izlazi ispod grupe.</summary>
    public void MoveSelectedDown()
    {
        if (_selected?.Parent is not { } parent) return;
        var node = _selected;
        int i = parent.Children.IndexOf(node);
        if (i < parent.Children.Count - 1) parent.Children.Move(i, i + 1);
        else if (parent.Parent is { } grand)
        {
            parent.Children.RemoveAt(i);
            grand.Children.Insert(grand.Children.IndexOf(parent) + 1, node);
        }
        RefreshRows();
        Select(node);
    }

    public bool CanDrop(MacroNode node, MacroNode target, DropPosition pos)
    {
        if (ReferenceEquals(node, target)) return false;
        if (node is GroupNode g && (ReferenceEquals(target, g) || target.IsDescendantOf(g))) return false;
        if (pos == DropPosition.Into) return target is GroupNode;
        return target.Parent != null;
    }

    public void MoveNode(MacroNode node, MacroNode target, DropPosition pos)
    {
        if (!CanDrop(node, target, pos) || node.Parent is not { } oldParent) return;

        GroupNode newParent;
        int index;
        if (pos == DropPosition.Into)
        {
            newParent = (GroupNode)target;
            index = newParent.Children.Count;
        }
        else
        {
            newParent = target.Parent!;
            index = newParent.Children.IndexOf(target) + (pos == DropPosition.After ? 1 : 0);
        }

        int oldIndex = oldParent.Children.IndexOf(node);
        if (ReferenceEquals(oldParent, newParent))
        {
            if (oldIndex < index) index--;
            if (oldIndex != index) oldParent.Children.Move(oldIndex, index);
        }
        else
        {
            oldParent.Children.RemoveAt(oldIndex);
            newParent.Children.Insert(index, node);
        }
        newParent.IsExpanded = true;
        RefreshRows();
        Select(node);
    }

    // ---------------------------------------------------------------- jezik

    /// <summary>Jezik je promijenjen: osvjezi tekstove koji se racunaju u kodu.</summary>
    private void OnLanguageChanged()
    {
        foreach (var node in Root.AllNodes()) node.RefreshLocalized();
        RefreshLibrary();
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(DocumentName));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(EstimatedTime));
    }

    // ---------------------------------------------------------------- zapisnik

    public void AddLog(string text)
    {
        Log.Add(new LogEntry(DateTime.Now.ToString("HH:mm:ss"), text));
        while (Log.Count > 500) Log.RemoveAt(0);
    }

    public void ClearActive()
    {
        foreach (var s in Root.AllSteps()) s.IsActive = false;
    }
}
