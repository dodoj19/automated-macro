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

    public static void Click(MouseButtonKind b, int count)
    {
        for (int i = 0; i < count; i++)
        {
            ButtonDown(b);
            Thread.Sleep(20);
            ButtonUp(b);
            if (i < count - 1) Thread.Sleep(60);
        }
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

    private static INPUT MouseInput(uint flags) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, dwExtraInfo = Signature } },
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
