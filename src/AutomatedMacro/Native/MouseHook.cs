using System.Runtime.InteropServices;
using System.Windows.Threading;
using static AutomatedMacro.Win32;

namespace AutomatedMacro;

public readonly record struct RawMouseEvent(
    MouseButtonKind Button, bool IsDown, int X, int Y, bool OverOwnWindow, uint Time);

/// <summary>
/// Globalni hook misa (WH_MOUSE_LL). Vidi klikove u svim programima.
/// Mora se pokrenuti na UI dretvi (treba joj petlja poruka).
/// </summary>
public sealed class MouseHook : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly LowLevelMouseProc _proc; // drzimo referencu da je GC ne pokupi
    private IntPtr _hook;
    private bool _swallowUp;

    /// <summary>Dogadjaji stizu asinkrono na UI dretvu.</summary>
    public event Action<RawMouseEvent>? ButtonEvent;

    /// <summary>Ako je postavljeno i vrati true za pritisak, klik se "proguta" i ne stigne do programa ispod.</summary>
    public Func<RawMouseEvent, bool>? Swallow { get; set; }

    public MouseHook()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = HookProc;
    }

    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException(Loc.F("Err_Hook", Marshal.GetLastWin32Error()));
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _swallowUp = false;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            (MouseButtonKind button, bool down)? kind = msg switch
            {
                WM_LBUTTONDOWN => (MouseButtonKind.Left, true),
                WM_LBUTTONUP => (MouseButtonKind.Left, false),
                WM_RBUTTONDOWN => (MouseButtonKind.Right, true),
                WM_RBUTTONUP => (MouseButtonKind.Right, false),
                WM_MBUTTONDOWN => (MouseButtonKind.Middle, true),
                WM_MBUTTONUP => (MouseButtonKind.Middle, false),
                _ => null,
            };

            if (kind is { } k)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                // Nase vlastite reprodukcije se ne snimaju.
                if (info.dwExtraInfo != InputSender.Signature)
                {
                    var ev = new RawMouseEvent(k.button, k.down, info.pt.X, info.pt.Y,
                        IsOwnWindowAt(info.pt.X, info.pt.Y), info.time);

                    bool swallow = false;
                    if (k.down)
                    {
                        swallow = Swallow?.Invoke(ev) == true;
                        _swallowUp = swallow;
                    }
                    else if (_swallowUp)
                    {
                        swallow = true;
                        _swallowUp = false;
                    }

                    _dispatcher.BeginInvoke(() => ButtonEvent?.Invoke(ev));
                    if (swallow) return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}

public enum CaptureKind { Click, Drag }

public sealed record CapturedAction(CaptureKind Kind, MouseButtonKind Button, int X, int Y, int Clicks = 1, int X2 = 0, int Y2 = 0)
{
    public string Describe() => Kind == CaptureKind.Drag
        ? Loc.F("Capture_Drag", X, Y, X2, Y2)
        : $"{StepNode.ClickLabel(Button, Clicks)} · {X}, {Y}";
}

/// <summary>Od sirovih pritisaka/otpustanja slaze klik, dvoklik ili povlacenje.</summary>
public sealed class ClickRecognizer
{
    private const int DragThreshold = 12;
    private const int DoubleClickSlop = 6;

    private RawMouseEvent? _down;
    private CapturedAction? _last;
    private uint _lastUpTime;

    public event Action<CapturedAction>? Captured;

    public void Reset()
    {
        _down = null;
        _last = null;
    }

    public void Feed(RawMouseEvent e)
    {
        if (e.IsDown)
        {
            _down = e.OverOwnWindow ? null : e;
            return;
        }

        if (_down is not { } d || d.Button != e.Button) return;
        _down = null;

        CapturedAction action;
        int dist = Math.Max(Math.Abs(e.X - d.X), Math.Abs(e.Y - d.Y));
        if (dist > DragThreshold)
        {
            action = new CapturedAction(CaptureKind.Drag, e.Button, d.X, d.Y, 1, e.X, e.Y);
        }
        else if (_last is { Kind: CaptureKind.Click, Clicks: 1 } prev
                 && prev.Button == e.Button
                 && unchecked(e.Time - _lastUpTime) <= Win32.GetDoubleClickTime()
                 && Math.Abs(prev.X - d.X) <= DoubleClickSlop
                 && Math.Abs(prev.Y - d.Y) <= DoubleClickSlop)
        {
            action = prev with { Clicks = 2 };
        }
        else
        {
            action = new CapturedAction(CaptureKind.Click, e.Button, d.X, d.Y);
        }

        _last = action;
        _lastUpTime = e.Time;
        Captured?.Invoke(action);
    }
}
