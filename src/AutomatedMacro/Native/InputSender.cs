using System.Runtime.InteropServices;
using static AutomatedMacro.Win32;

namespace AutomatedMacro;

/// <summary>Salje mis i tipkovnicu kroz SendInput (kao da ih korisnik stvarno koristi).</summary>
internal static class InputSender
{
    /// <summary>Oznaka na svim nasim umjetnim dogadjajima (dwExtraInfo).</summary>
    public static readonly IntPtr Signature = new(0x4D41_4352);

    public const ushort VK_RETURN = 0x0D, VK_TAB = 0x09, VK_DELETE = 0x2E, VK_A = 0x41;
    private const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public static void MoveTo(int x, int y)
    {
        SetCursorPos(x, y);
        // Relativni pomak 0,0 generira pravi "mouse move" dogadjaj (hover efekti u preglednicima).
        Send(MouseInput(MOUSEEVENTF_MOVE));
    }

    public static void ButtonDown(MouseButtonKind b) => Send(MouseInput(b switch
    {
        MouseButtonKind.Right => MOUSEEVENTF_RIGHTDOWN,
        MouseButtonKind.Middle => MOUSEEVENTF_MIDDLEDOWN,
        _ => MOUSEEVENTF_LEFTDOWN,
    }));

    public static void ButtonUp(MouseButtonKind b) => Send(MouseInput(b switch
    {
        MouseButtonKind.Right => MOUSEEVENTF_RIGHTUP,
        MouseButtonKind.Middle => MOUSEEVENTF_MIDDLEUP,
        _ => MOUSEEVENTF_LEFTUP,
    }));

    public static void Click(MouseButtonKind b, int count, int holdMs = 20, int gapMs = 60)
    {
        for (int i = 0; i < count; i++)
        {
            ButtonDown(b);
            Thread.Sleep(holdMs);
            ButtonUp(b);
            if (i < count - 1) Thread.Sleep(gapMs);
        }
    }

    // ------------------------------------------------------------ nacin za igre
    //
    // SetCursorPos premjesta kursor, ali igre koje citaju raw input (WM_INPUT), npr. Roblox,
    // tada ne vide nikakav pomak: za njih mis ostaje na staroj poziciji pa klik "promasi" gumb.
    // Zato se ovdje mis pomice kroz SendInput (apsolutno + nekoliko relativnih koraka),
    // prije pritiska se ceka nekoliko slicica, a tipka se drzi dulje od jedne slicice.

    private const int FrameMs = 16;

    /// <summary>Premjesti mis kao pravi korisnik: dolazak s malog odmaka, vidljivo i u raw inputu.</summary>
    public static void MoveLikeMouse(int x, int y)
    {
        const int offset = 6, steps = 3;
        MoveAbsolute(x + offset, y + offset);
        Thread.Sleep(FrameMs);
        for (int i = 0; i < steps; i++)
        {
            Send(MouseInput(MOUSEEVENTF_MOVE, -offset / steps, -offset / steps));
            Thread.Sleep(FrameMs);
        }
        // Relativni koraci ovise o brzini/ubrzanju pokazivaca, pa na kraju tocno poravnaj.
        MoveExact(x, y);
    }

    /// <summary>Relativni pomak prema tocki (raw input vidi pravi pomak).</summary>
    public static void MoveTowards(int x, int y)
    {
        GetCursorPos(out var p);
        if (p.X != x || p.Y != y) Send(MouseInput(MOUSEEVENTF_MOVE, x - p.X, y - p.Y));
    }

    /// <summary>Apsolutni pomak kroz SendInput tocno na tocku.</summary>
    public static void MoveExact(int x, int y)
    {
        MoveAbsolute(x, y);
        GetCursorPos(out var p);
        if (p.X != x || p.Y != y) SetCursorPos(x, y);
    }

    private static void MoveAbsolute(int x, int y)
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = Math.Max(2, GetSystemMetrics(SM_CXVIRTUALSCREEN)), vh = Math.Max(2, GetSystemMetrics(SM_CYVIRTUALSCREEN));
        int nx = (int)Math.Round((x - vx) * 65535.0 / (vw - 1));
        int ny = (int)Math.Round((y - vy) * 65535.0 / (vh - 1));
        Send(MouseInput(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, nx, ny));
    }

    /// <summary>
    /// Ako prozor ispod tocke nije aktivan, aktivira ga (klik na neaktivan prozor igre
    /// cesto samo prebaci fokus). Vraca naslov aktiviranog prozora ili null.
    /// </summary>
    public static string? ActivateWindowAt(int x, int y)
    {
        var root = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), GA_ROOT);
        if (root == IntPtr.Zero || root == GetForegroundWindow()) return null;
        SetForegroundWindow(root);
        Thread.Sleep(150);
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(root, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Klik za igre: aktiviraj prozor, dovedi mis, pricekaj, drzi tipku.</summary>
    public static string? GameClick(int x, int y, MouseButtonKind b, int count, int holdMs)
    {
        var activated = ActivateWindowAt(x, y);
        MoveLikeMouse(x, y);
        Thread.Sleep(holdMs);                       // "hover": igra vidi mis na gumbu
        Click(b, count, holdMs, Math.Max(60, holdMs));
        return activated;
    }

    public static void KeyDown(ushort vk) => Send(KeyInput(vk, false));
    public static void KeyUp(ushort vk) => Send(KeyInput(vk, true));

    public static void KeyPress(ushort vk)
    {
        KeyDown(vk);
        Thread.Sleep(15);
        KeyUp(vk);
    }

    public static void KeyCombo(ushort vk, bool ctrl, bool shift, bool alt, bool win)
    {
        var mods = new List<ushort>();
        if (ctrl) mods.Add(VK_CONTROL);
        if (shift) mods.Add(VK_SHIFT);
        if (alt) mods.Add(VK_MENU);
        if (win) mods.Add(VK_LWIN);
        try
        {
            foreach (var m in mods) { KeyDown(m); Thread.Sleep(10); }
            KeyPress(vk);
        }
        finally
        {
            for (int i = mods.Count - 1; i >= 0; i--) { KeyUp(mods[i]); Thread.Sleep(10); }
        }
    }

    /// <summary>Upisuje tekst znak po znak (Unicode, pa rade i č, ć, š, ž, đ).</summary>
    public static void TypeText(string text, int charDelayMs, CancellationToken ct)
    {
        for (int i = 0; i < text.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            char c = text[i];
            if (c == '\r') continue;
            if (c == '\n') KeyPress(VK_RETURN);
            else if (c == '\t') KeyPress(VK_TAB);
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length)
            {
                char low = text[++i];
                Send(UnicodeInput(c, false), UnicodeInput(low, false), UnicodeInput(c, true), UnicodeInput(low, true));
            }
            else
            {
                Send(UnicodeInput(c, false), UnicodeInput(c, true));
            }
            if (charDelayMs > 0) Thread.Sleep(charDelayMs);
        }
    }

    private static INPUT MouseInput(uint flags, int dx = 0, int dy = 0) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags, dwExtraInfo = Signature } },
    };

    private static INPUT KeyInput(ushort vk, bool up)
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = flags, dwExtraInfo = Signature },
            },
        };
    }

    private static INPUT UnicodeInput(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), dwExtraInfo = Signature },
        },
    };

    // Strelice, Home/End, PgUp/PgDn, Insert/Delete, Win tipke i NumLock trebaju "extended" zastavicu.
    private static bool IsExtended(ushort vk) =>
        vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5;

    private static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, InputSize);
}
