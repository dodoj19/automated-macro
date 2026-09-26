using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AutomatedMacro;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly MouseHook _hook = new();
    private readonly ClickRecognizer _recognizer = new();
    private HotkeyManager? _hotkeys;
    private RecorderWindow? _recorder;
    private CancellationTokenSource? _cts;
    private OverlayWindow? _overlay;
    private MacroNode? _activeStep;
    private TaskCompletionSource<(int X, int Y)?>? _pickTcs;
    private RawMouseEvent? _pickDown;
    private WindowState _stateBeforeMinimize = WindowState.Normal;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _vm = new MainViewModel(settings);
        DataContext = _vm;
        FitToScreen();

        _ = new RowDragDrop(StepList, _vm);
        _hook.ButtonEvent += OnHookEvent;
        _recognizer.Captured += a => _recorder?.SetCapture(a);
        _vm.Log.CollectionChanged += (_, _) => LogScroll.ScrollToEnd();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Selected) && _vm.Selected != null)
                StepList.ScrollIntoView(_vm.Selected);
        };
        SourceInitialized += (_, _) => _hotkeys = new HotkeyManager();
        _vm.AddLog(Loc.T("Log_Ready"));
    }

    // ================================================================ datoteke

    public void TryOpen(string path, bool quiet = false)
    {
        try
        {
            _vm.OpenFile(path);
            _vm.App.LastFile = path;
        }
        catch (Exception ex)
        {
            _vm.AddLog(Loc.F("Log_OpenFailed", Path.GetFileName(path), ex.Message));
            if (!quiet)
                MessageBox.Show(this, Loc.F("Err_OpenFile", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Pita za spremanje nespremljenih promjena. False = korisnik je odustao.</summary>
    private bool ConfirmDiscard()
    {
        CommitEdits();
        if (!_vm.IsDirty) return true;
        var r = MessageBox.Show(this, Loc.F("AskSave", _vm.DocumentName), "Automated Macro",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private bool Save(bool saveAs = false)
    {
        CommitEdits();
        var path = _vm.FilePath;
        if (saveAs || path == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = ProcedureFile.DialogFilter,
                DefaultExt = ProcedureFile.Extension,
                FileName = _vm.DocumentName,
                InitialDirectory = path != null ? Path.GetDirectoryName(path) : EnsureLibrary(),
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }

        try
        {
            _vm.SaveFile(path);
            _vm.App.LastFile = path;
            _vm.App.Save();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.F("Err_Save", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Potvrdi vrijednost iz polja koje je trenutno u fokusu (npr. prije Ctrl+S).</summary>
    private static void CommitEdits()
    {
        if (Keyboard.FocusedElement is TextBox tb)
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private string EnsureLibrary()
    {
        var folder = _vm.App.EffectiveLibraryFolder;
        try { Directory.CreateDirectory(folder); } catch { /* javit ce se kod spremanja */ }
        return folder;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        _vm.NewProcedure();
        _vm.AddLog(Loc.T("Log_NewProcedure"));
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = ProcedureFile.DialogFilter, InitialDirectory = EnsureLibrary() };
        if (dlg.ShowDialog(this) == true) TryOpen(dlg.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => Save(saveAs: true);

    private void CloseDocument_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmDiscard()) _vm.NewProcedure();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsPopup.IsOpen = true;

    private void About_Click(object sender, RoutedEventArgs e) => AboutPopup.IsOpen = true;

    private void ChangeLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { InitialDirectory = EnsureLibrary(), Title = Loc.T("LibraryFolder") };
        if (dlg.ShowDialog(this) != true) return;
        _vm.App.LibraryFolder = dlg.FolderName;
        _vm.App.Save();
        _vm.RefreshLibrary();
    }

    private void OpenLibraryFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{EnsureLibrary()}\"") { UseShellExecute = true });

    private void ResetLibrary_Click(object sender, RoutedEventArgs e)
    {
        _vm.App.LibraryFolder = null;
        _vm.App.Save();
        _vm.RefreshLibrary();
    }

    // ================================================================ popis makroa (lijevo)

    private void NewMacro_Click(object sender, RoutedEventArgs e)
    {
        var folder = (LibraryTree.SelectedItem as LibraryItem) switch
        {
            { Kind: LibraryKind.Folder } f => f.FullPath,
            { Kind: LibraryKind.File } f => Path.GetDirectoryName(f.FullPath)!,
            _ => EnsureLibrary(),
        };
        CreateMacroIn(folder);
    }

    private void CreateMacroIn(string folder)
    {
        if (!ConfirmDiscard()) return;
        var name = InputDialog.Ask(this, Loc.T("NewProcedure"), Loc.T("ProcedureName"), Loc.T("NewProcedure"));
        if (name == null) return;
        Guard(() =>
        {
            Directory.CreateDirectory(folder);
            var path = MacroLibrary.UniqueFilePath(folder, name);
            _vm.NewProcedure();
            _vm.SaveFile(path);
            _vm.App.LastFile = path;
        });
    }

    private void Library_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (RowDragDrop.FindAncestor<ToggleButton>(source) != null) return;
        if (RowDragDrop.FindAncestor<TreeViewItem>(source)?.DataContext is not LibraryItem { IsFile: true, IsCurrent: false } item) return;
        if (!ConfirmDiscard()) return;
        TryOpen(item.FullPath);
    }

    private void Library_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var tvi = RowDragDrop.FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (tvi?.DataContext is not LibraryItem item) return;
        item.IsSelected = true;
        e.Handled = true;

        var menu = new ContextMenu { PlacementTarget = tvi };
        void Add(string header, Action action, string? glyph = null)
        {
            var mi = new MenuItem { Header = header };
            if (glyph != null) mi.Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14 };
            mi.Click += (_, _) => Guard(action);
            menu.Items.Add(mi);
        }

        var root = EnsureLibrary();
        switch (item.Kind)
        {
            case LibraryKind.File:
                Add(Loc.T("OpenItem"), () => { if (ConfirmDiscard()) TryOpen(item.FullPath); }, "");
                Add(Loc.T("Rename"), () => RenameFile(item), "");
                Add(Loc.T("Duplicate"), () => DuplicateFile(item), "");
                Add(Loc.T("DeleteToTrash"), () => TrashFile(item, root), "");
                menu.Items.Add(new Separator());
                Add(Loc.T("ShowInExplorer"), () => Process.Start("explorer.exe", $"/select,\"{item.FullPath}\""), "");
                break;

            case LibraryKind.Folder:
                Add(Loc.T("NewProcedureHere"), () => CreateMacroIn(item.FullPath), "");
                Add(Loc.T("NewFolderMenu"), () => NewFolder(item.FullPath), "");
                if (!string.Equals(item.FullPath.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    Add(Loc.T("Rename"), () => RenameFolder(item), "");
                    Add(Loc.T("DeleteEmptyFolder"), () => DeleteEmptyFolder(item), "");
                }
                menu.Items.Add(new Separator());
                Add(Loc.T("Refresh"), _vm.RefreshLibrary, "");
                Add(Loc.T("ShowInExplorer"), () => Process.Start("explorer.exe", $"\"{item.FullPath}\""), "");
                break;

            case LibraryKind.Trash:
                Add(Loc.T("EmptyTrash"), () => EmptyTrash(item), "");
                break;

            case LibraryKind.TrashedFile:
                Add(Loc.T("Restore"), () => { MacroLibrary.Restore(root, item.FullPath); _vm.RefreshLibrary(); }, "");
                Add(Loc.T("DeleteForever"), () => DeleteForever(item), "");
                break;
        }
        menu.IsOpen = true;
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            _vm.RefreshLibrary();
        }
    }

    private void RenameFile(LibraryItem item)
    {
        var name = InputDialog.Ask(this, Loc.T("RenameTitle"), Loc.T("NewProcedureName"), item.Name);
        if (name == null) return;
        var target = Path.Combine(Path.GetDirectoryName(item.FullPath)!, MacroLibrary.SafeName(name) + ProcedureFile.Extension);
        if (string.Equals(target, item.FullPath, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(target)) throw new IOException(Loc.T("Err_NameExists"));
        File.Move(item.FullPath, target);
        if (item.IsCurrent)
        {
            _vm.FileMoved(target);
            _vm.App.LastFile = target;
        }
        _vm.RefreshLibrary();
    }

    private void DuplicateFile(LibraryItem item)
    {
        var target = MacroLibrary.UniqueFilePath(Path.GetDirectoryName(item.FullPath)!, item.Name + " - " + Loc.T("CopySuffix"));
        File.Copy(item.FullPath, target);
        _vm.RefreshLibrary();
    }

    private void TrashFile(LibraryItem item, string root)
    {
        if (MessageBox.Show(this, Loc.F("AskTrash", item.Name), Loc.T("Delete"), MessageBoxButton.OKCancel,
                MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (item.IsCurrent)
        {
            _vm.IsDirty = false;
            _vm.NewProcedure();
        }
        MacroLibrary.MoveToTrash(root, item.FullPath);
        _vm.RefreshLibrary();
    }

    private void NewFolder(string parent)
    {
        var name = InputDialog.Ask(this, Loc.T("NewFolderTitle"), Loc.T("FolderName"), Loc.T("NewFolderTitle"));
        if (name == null) return;
        Directory.CreateDirectory(MacroLibrary.UniqueFolderPath(parent, name));
        _vm.RefreshLibrary();
    }

    private void RenameFolder(LibraryItem item)
    {
        var name = InputDialog.Ask(this, Loc.T("RenameFolderTitle"), Loc.T("NewFolderName"), item.Name);
        if (name == null) return;
        var target = Path.Combine(Path.GetDirectoryName(item.FullPath.TrimEnd('\\'))!, MacroLibrary.SafeName(name));
        if (Directory.Exists(target)) throw new IOException(Loc.T("Err_FolderExists"));
        Directory.Move(item.FullPath, target);
        if (_vm.FilePath is { } open && open.StartsWith(item.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            _vm.FileMoved(target + open[item.FullPath.Length..]);
        _vm.RefreshLibrary();
    }

    private void DeleteEmptyFolder(LibraryItem item)
    {
        if (Directory.EnumerateFileSystemEntries(item.FullPath).Any())
            throw new IOException(Loc.T("Err_FolderNotEmpty"));
        Directory.Delete(item.FullPath);
        _vm.RefreshLibrary();
    }

    private void EmptyTrash(LibraryItem trash)
    {
        if (trash.Children.Count == 0) return;
        if (MessageBox.Show(this, Loc.F("AskEmptyTrash", trash.Children.Count), Loc.T("EmptyTrash"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        foreach (var f in trash.Children) File.Delete(f.FullPath);
        _vm.RefreshLibrary();
    }

    private void DeleteForever(LibraryItem item)
    {
        if (MessageBox.Show(this, Loc.F("AskDeleteForever", item.Name), Loc.T("DeleteForeverTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        File.Delete(item.FullPath);
        _vm.RefreshLibrary();
    }

    // ================================================================ uredjivanje koraka

    private static (int X, int Y) ScreenCenter()
    {
        var m = Win32.Monitors().FirstOrDefault(m => m.Primary);
        return ((m.Bounds.Left + m.Bounds.Right) / 2, (m.Bounds.Top + m.Bounds.Bottom) / 2);
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e) => _vm.Insert(_vm.CreateGroup());

    private void AddStepMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = AddStepButton.ContextMenu!;
        menu.PlacementTarget = AddStepButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void AddClick_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = ScreenCenter();
        _vm.Insert(new ClickStep { Name = Loc.T("Click"), X = x, Y = y });
    }

    private void AddText_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = ScreenCenter();
        _vm.Insert(new TextStep { Name = Loc.T("Kind_Text"), X = x, Y = y });
    }

    private void AddWait_Click(object sender, RoutedEventArgs e) => _vm.Insert(new WaitStep { Name = Loc.T("Wait"), Ms = 1000 });
    private void AddKey_Click(object sender, RoutedEventArgs e) => _vm.Insert(new KeyStep { Name = Loc.T("Key") });

    private void AddDrag_Click(object sender, RoutedEventArgs e)
    {
        var (x, y) = ScreenCenter();
        _vm.Insert(new DragStep { Name = Loc.T("Kind_Drag"), X = x, Y = y, X2 = x + 200, Y2 = y });
    }

    private void Cut_Click(object sender, RoutedEventArgs e) => _vm.CutSelected();
    private void Copy_Click(object sender, RoutedEventArgs e) => _vm.CopySelected();
    private void Paste_Click(object sender, RoutedEventArgs e) => _vm.Paste();
    private void Duplicate_Click(object sender, RoutedEventArgs e) => _vm.DuplicateSelected();
    private void MoveUp_Click(object sender, RoutedEventArgs e) => _vm.MoveSelectedUp();
    private void MoveDown_Click(object sender, RoutedEventArgs e) => _vm.MoveSelectedDown();
    private void ToggleEnabled_Click(object sender, RoutedEventArgs e) => _vm.ToggleEnabledSelected();
    private void ExpandAll_Click(object sender, RoutedEventArgs e) => _vm.SetAllExpanded(true);
    private void CollapseAll_Click(object sender, RoutedEventArgs e) => _vm.SetAllExpanded(false);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is GroupNode { StepCount: > 0 } g &&
            MessageBox.Show(this, Loc.F("AskDeleteGroup", g.Name, g.StepCount), Loc.T("DeleteGroupTitle"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        _vm.DeleteSelected();
        StepList.Focus();
    }

    private void StepList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_vm.IsIdle) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool handled = true;

        if (mods == ModifierKeys.Alt && key == Key.Up) _vm.MoveSelectedUp();
        else if (mods == ModifierKeys.Alt && key == Key.Down) _vm.MoveSelectedDown();
        else if (mods == ModifierKeys.Control && key == Key.C) _vm.CopySelected();
        else if (mods == ModifierKeys.Control && key == Key.X) _vm.CutSelected();
        else if (mods == ModifierKeys.Control && key == Key.V) _vm.Paste();
        else if (mods == ModifierKeys.Control && key == Key.D) _vm.DuplicateSelected();
        else if (mods == ModifierKeys.None && key == Key.Delete) Delete_Click(sender, e);
        else if (mods == ModifierKeys.None && key == Key.Space) _vm.ToggleEnabledSelected();
        else if (mods == ModifierKeys.None && key == Key.F2) { NameBox.Focus(); NameBox.SelectAll(); }
        else if (mods == ModifierKeys.None && key == Key.Right && _vm.Selected is GroupNode { IsExpanded: false } g1) g1.IsExpanded = true;
        else if (mods == ModifierKeys.None && key == Key.Left && _vm.Selected is GroupNode { IsExpanded: true } g2) g2.IsExpanded = false;
        else if (mods == ModifierKeys.None && key == Key.Left && _vm.Selected?.Parent is { IsRoot: false } parent) _vm.Select(parent);
        else handled = false;

        if (handled)
        {
            e.Handled = true;
            FocusSelectedRow();
        }
    }

    private void FocusSelectedRow()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_vm.Selected != null && StepList.ItemContainerGenerator.ContainerFromItem(_vm.Selected) is ListBoxItem row)
                row.Focus();
        });
    }

    private void StepList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowDragDrop.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not MacroNode node) return;
        if (RowDragDrop.FindAncestor<ToggleButton>(e.OriginalSource as DependencyObject) != null) return;
        if (node is GroupNode g) g.IsExpanded = !g.IsExpanded;
        else { NameBox.Focus(); NameBox.SelectAll(); }
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is GroupNode g && sender is FrameworkElement { DataContext: string hex })
            g.Color = hex;
        ColorToggle.IsChecked = false;
    }

    private void ColorPopup_Closed(object? sender, EventArgs e) => ColorToggle.IsChecked = false;

    private void WaitPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is WaitStep w && sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var ms))
            w.Ms = ms;
    }

    private void KeyCapture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (_vm.Selected is not KeyStep step) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed or Key.None) return;
        var mods = Keyboard.Modifiers;
        step.SetCombo(KeyInterop.VirtualKeyFromKey(key),
            mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Shift),
            mods.HasFlag(ModifierKeys.Alt), mods.HasFlag(ModifierKeys.Windows));
    }

    private void KeyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is KeyStep step && sender is FrameworkElement { DataContext: KeyPreset p })
            step.SetCombo(p.Vk, p.Ctrl, p.Shift, p.Alt, false);
    }

    private void GenerateExample_Click(object sender, RoutedEventArgs e)
    {
        CommitEdits();
        if (_vm.Selected is TextStep t)
            _vm.ExampleText = t.Prefix + TextGenerator.Unique(t.MinLength, t.MaxLength, t.Charset) + t.Suffix;
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _vm.Log.Clear();

    private void CopyLog_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(string.Join(Environment.NewLine, _vm.Log.Select(l => $"[{l.Time}] {l.Text}")));

    // ================================================================ tipkovnica

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (!_vm.IsIdle)
        {
            if (key == Key.F10 && _vm.IsRunning) { StopRun(); e.Handled = true; }
            return;
        }

        bool handled = true;
        if (mods == ModifierKeys.None && key == Key.F9) StartRecording();
        else if (mods == ModifierKeys.None && key == Key.F5) Run_Click(sender, e);
        else if (mods == ModifierKeys.Control && key == Key.N) New_Click(sender, e);
        else if (mods == ModifierKeys.Control && key == Key.O) Open_Click(sender, e);
        else if (mods == ModifierKeys.Control && key == Key.S) Save();
        else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.S) Save(saveAs: true);
        else handled = false;
        e.Handled = handled;
    }

    // ================================================================ snimanje

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsRecording) FinishRecording();
        else StartRecording();
    }

    private void StartRecording()
    {
        if (!_vm.IsIdle || _hotkeys == null) return;
        CommitEdits();
        var target = _vm.Selected as GroupNode ?? _vm.Selected?.Parent ?? _vm.Root;

        _recognizer.Reset();
        try { _hook.Start(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("Err_RecordTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _vm.Mode = AppMode.Recording;
        _vm.RunInfo = Loc.T("RecordInfo");
        bool f8 = _hotkeys.Register(Hotkeys.SaveStepId, Hotkeys.F8, () => _recorder?.SaveStep());
        bool f9 = _hotkeys.Register(Hotkeys.FinishRecordId, Hotkeys.F9, FinishRecording);
        if (!f8 || !f9) _vm.AddLog(Loc.T("Warn_HotkeysRecord"));

        _recorder = new RecorderWindow(_vm, target);
        _recorder.Finished += FinishRecording;
        _recorder.Show();
        _vm.AddLog(Loc.T("Log_RecordStarted"));
        if (_vm.App.MinimizeDuringRecord) MinimizeMain();
    }

    private void FinishRecording()
    {
        if (!_vm.IsRecording) return;
        _hook.Stop();
        _hotkeys?.Unregister(Hotkeys.SaveStepId);
        _hotkeys?.Unregister(Hotkeys.FinishRecordId);
        int count = _recorder?.SavedCount ?? 0;
        _recorder?.CloseSilently();
        _recorder = null;
        _vm.Mode = AppMode.Idle;
        _vm.RunInfo = "";
        _vm.AddLog(Loc.F("Log_RecordFinished", count));
        RestoreMain();
    }

    private void OnHookEvent(RawMouseEvent e)
    {
        switch (_vm.Mode)
        {
            case AppMode.Recording:
                _recognizer.Feed(e);
                break;
            case AppMode.Picking:
                if (e.IsDown && !e.OverOwnWindow) _pickDown = e;
                else if (!e.IsDown && _pickDown is { } d)
                {
                    _pickDown = null;
                    _pickTcs?.TrySetResult((d.X, d.Y));
                }
                break;
        }
    }

    // ================================================================ odabir tocke na ekranu

    private async void PickPoint_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsIdle || _vm.Selected is not StepNode step) return;
        CommitEdits();
        bool end = (sender as FrameworkElement)?.Tag as string == "end";

        var point = await PickPointAsync();
        if (point is not { } p) return;

        switch (step)
        {
            case ClickStep c: c.X = p.X; c.Y = p.Y; break;
            case TextStep t: t.X = p.X; t.Y = p.Y; break;
            case DragStep d when end: d.X2 = p.X; d.Y2 = p.Y; break;
            case DragStep d: d.X = p.X; d.Y = p.Y; break;
        }
        _vm.AddLog(Loc.F("Log_PointPicked", p.X, p.Y));
    }

    private async Task<(int X, int Y)?> PickPointAsync()
    {
        if (_hotkeys == null) return null;
        _vm.Mode = AppMode.Picking;
        _pickDown = null;
        _pickTcs = new TaskCompletionSource<(int X, int Y)?>();
        _hook.Swallow = ev => _vm.Mode == AppMode.Picking && !ev.OverOwnWindow;
        try { _hook.Start(); }
        catch (Exception ex)
        {
            _vm.Mode = AppMode.Idle;
            _hook.Swallow = null;
            MessageBox.Show(this, ex.Message, Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        _hotkeys.Register(Hotkeys.CancelPickId, Hotkeys.Escape, () => _pickTcs?.TrySetResult(null));

        var overlay = new OverlayWindow(Color.FromRgb(0xF5, 0xA5, 0x24));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            var (x, y) = Win32.CursorPosition();
            overlay.SetText(Loc.F("Pick_Title", x, y), Loc.T("Pick_Cancel"));
            overlay.MoveNear(x, y);
        };
        MinimizeMain();
        timer.Start();
        try
        {
            return await _pickTcs.Task;
        }
        finally
        {
            timer.Stop();
            overlay.Close();
            _hook.Stop();
            _hook.Swallow = null;
            _hotkeys.Unregister(Hotkeys.CancelPickId);
            _pickTcs = null;
            _vm.Mode = AppMode.Idle;
            RestoreMain();
        }
    }

    // ================================================================ izvodjenje

    private void Run_Click(object sender, RoutedEventArgs e) =>
        _ = RunAsync(_vm.Root.Children.ToList(), singlePass: false, Loc.F("Log_RunStart", _vm.DocumentName));

    private void RunSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is { } node) _ = RunAsync([node], singlePass: true, Loc.F("Log_RunOnly", node.Name));
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsRunning) StopRun();
        else if (_vm.IsRecording) FinishRecording();
    }

    private void StopRun() => _cts?.Cancel();

    private async Task RunAsync(IReadOnlyList<MacroNode> nodes, bool singlePass, string label)
    {
        if (!_vm.IsIdle || _hotkeys == null) return;
        CommitEdits();

        var runner = new MacroRunner(nodes, _vm.Settings, singlePass);
        if (runner.StepCount == 0)
        {
            _vm.AddLog(Loc.T("Run_NoSteps"));
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _vm.Mode = AppMode.Running;
        if (!singlePass) _vm.ExecutedLoops = 0;
        if (!_hotkeys.Register(Hotkeys.StopRunId, Hotkeys.F10, StopRun))
            _vm.AddLog(Loc.T("Warn_F10"));

        _overlay = new OverlayWindow(Color.FromRgb(0x7C, 0x5C, 0xFF));
        _overlay.SetText(Loc.T("Overlay_Starting"), Loc.T("Overlay_F10"));
        _overlay.ShowTopCenter();

        runner.Progress += p => Dispatcher.Invoke(() => OnProgress(p));
        runner.Log += s => Dispatcher.Invoke(() => _vm.AddLog(s));
        if (!singlePass) runner.CycleCompleted += c => Dispatcher.Invoke(() => _vm.ExecutedLoops = c);

        _vm.AddLog(label);
        if (_vm.App.MinimizeDuringRun && !singlePass) MinimizeMain();

        try
        {
            await Task.Run(() => runner.Run(token));
            _vm.AddLog(Loc.T("Log_RunDone"));
        }
        catch (OperationCanceledException)
        {
            _vm.AddLog(Loc.T("Log_RunStopped"));
        }
        catch (Exception ex)
        {
            _vm.AddLog(Loc.F("Log_Error", ex.Message));
        }
        finally
        {
            _hotkeys.Unregister(Hotkeys.StopRunId);
            _overlay?.Close();
            _overlay = null;
            if (_activeStep != null) _activeStep.IsActive = false;
            _activeStep = null;
            _cts.Dispose();
            _cts = null;
            _vm.Mode = AppMode.Idle;
            _vm.RunInfo = "";
            RestoreMain();
        }
    }

    private void OnProgress(RunProgress p)
    {
        if (_activeStep != null) _activeStep.IsActive = false;
        _activeStep = p.Step;
        if (p.Step != null)
        {
            p.Step.IsActive = true;
            if (WindowState != WindowState.Minimized && _vm.Rows.Contains(p.Step)) StepList.ScrollIntoView(p.Step);
        }

        string cycles = p.TotalCycles == 0 ? "∞" : p.TotalCycles.ToString();
        if (p.Cycle == 0)
        {
            _vm.RunInfo = p.Text;
            _overlay?.SetText(p.Text, Loc.T("Overlay_SwitchTarget"));
        }
        else
        {
            var head = Loc.F("RunHead", p.Cycle, cycles, p.StepIndex, p.StepCount);
            _vm.RunInfo = head;
            _overlay?.SetText(head, $"{p.Text}   ·   {Loc.T("Overlay_F10")}");
        }
    }

    // ================================================================ prozor

    /// <summary>Na manjim ekranima (ili uz veliko skaliranje) prozor ne smije biti veci od radne povrsine.</summary>
    private void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width * 0.94);
        Height = Math.Min(Height, area.Height * 0.94);
        if (area.Width < 1400 || area.Height < 860) WindowState = WindowState.Maximized;
    }

    private void MinimizeMain()
    {
        if (WindowState == WindowState.Minimized) return;
        _stateBeforeMinimize = WindowState;
        WindowState = WindowState.Minimized;
    }

    private void RestoreMain()
    {
        if (WindowState == WindowState.Minimized) WindowState = _stateBeforeMinimize;
        Activate();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_vm.IsRunning) StopRun();
        if (_vm.IsRecording) FinishRecording();
        _pickTcs?.TrySetResult(null);

        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        _vm.App.LastFile = _vm.FilePath;
        _vm.App.Save();
        _hook.Dispose();
        _hotkeys?.Dispose();
    }
}
