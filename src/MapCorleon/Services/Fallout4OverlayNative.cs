using System.Runtime.InteropServices;

namespace MapCorleon.Services;

internal static class Fallout4OverlayNative
{
    public const int HotkeyMessage = 0x0312;
    public const uint NoRepeat = 0x4000;
    private const int ExtendedStyle = -20;
    private const int PassiveStyles = 0x08000000 | 0x20;
    private const uint PositionOnly = 0x0001 | 0x0004 | 0x0010;

    public static bool TryGetGameAnchor(nint game, out Point anchor)
    {
        anchor = default;
        if (game == 0 || IsIconic(game) || !GetClientRect(game, out var rect) || rect.Right <= 0 || rect.Bottom <= 0)
            return false;

        anchor.X = rect.Right;
        return ClientToScreen(game, ref anchor);
    }

    public static void MoveToAnchor(nint overlay, Point anchor)
    {
        if (!GetWindowRect(overlay, out var rect)) return;
        var x = anchor.X - (rect.Right - rect.Left) - 16;
        var y = anchor.Y + 16;
        if (rect.Left != x || rect.Top != y)
            SetWindowPos(overlay, 0, x, y, 0, 0, PositionOnly);
    }

    public static void SetPassive(nint overlay, bool passive)
    {
        var styles = GetWindowLong(overlay, ExtendedStyle);
        SetWindowLong(overlay, ExtendedStyle, passive ? styles | PassiveStyles : styles & ~PassiveStyles);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint window, int index, int value);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
