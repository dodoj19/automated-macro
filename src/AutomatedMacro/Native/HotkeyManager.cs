using System.Windows.Interop;
using static AutomatedMacro.Win32;

namespace AutomatedMacro;

/// <summary>Globalne precice (rade i kad je fokus u drugom programu).</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();

    public HotkeyManager()
    {
        // Nevidljivi "message-only" prozor koji prima WM_HOTKEY.
        var p = new HwndSourceParameters("AutomatedMacroHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3),
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public bool Register(int id, uint vk, Action handler, uint modifiers = 0)
    {
        Unregister(id);
        if (!RegisterHotKey(_source.Handle, id, modifiers | MOD_NOREPEAT, vk)) return false;
        _handlers[id] = handler;
        return true;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id)) UnregisterHotKey(_source.Handle, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var h))
        {
            handled = true;
            h();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToList()) Unregister(id);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

public static class Hotkeys
{
    public const int SaveStepId = 1, FinishRecordId = 2, StopRunId = 3, CancelPickId = 4;
    public const uint F8 = 0x77, F9 = 0x78, F10 = 0x79, Escape = 0x1B;
}
