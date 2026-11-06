using System.Runtime.InteropServices;

namespace MapCorleon.Services;

internal static class WindowPlacement
{
    private const int ShowNormal = 1;
    private const int ShowMaximized = 3;

    public static WindowPlacementState? Capture(nint handle)
    {
        var placement = new NativePlacement { Length = Marshal.SizeOf<NativePlacement>() };
        if (handle == 0 || !GetWindowPlacement(handle, ref placement)) return null;
        return new WindowPlacementState(placement.Left, placement.Top, placement.Right, placement.Bottom,
            placement.ShowCommand == ShowMaximized);
    }

    public static void Restore(nint handle, WindowPlacementState saved)
    {
        if (handle == 0 || saved.Right <= saved.Left || saved.Bottom <= saved.Top) return;
        var placement = new NativePlacement
        {
            Length = Marshal.SizeOf<NativePlacement>(),
            ShowCommand = saved.Maximized ? ShowMaximized : ShowNormal,
            Left = saved.Left,
            Top = saved.Top,
            Right = saved.Right,
            Bottom = saved.Bottom
        };
        SetWindowPlacement(handle, ref placement);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint handle, ref NativePlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint handle, ref NativePlacement placement);
}
