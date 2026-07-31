using System.Runtime.InteropServices;

namespace AskAnywhere;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    public static System.Windows.Point GetCursorPosition()
    {
        GetCursorPos(out var p);
        return new System.Windows.Point(p.X, p.Y);
    }
}
