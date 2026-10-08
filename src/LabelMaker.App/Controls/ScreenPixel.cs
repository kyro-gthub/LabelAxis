using System.Runtime.InteropServices;
using LabelMaker.Core.Model;

namespace LabelMaker.App.Controls;

/// <summary>The eyedropper: the colour of the screen under the mouse pointer.</summary>
public static class ScreenPixel
{
    /// <summary>The colour under the pointer, fully opaque, or null when Windows cannot say where it is.</summary>
    public static ColorRgba? UnderPointer()
    {
        if (!GetCursorPos(out NativePoint p))
        {
            return null;
        }

        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            uint bgr = GetPixel(dc, p.X, p.Y);
            return new ColorRgba((byte)(bgr & 0xFF), (byte)((bgr >> 8) & 0xFF), (byte)((bgr >> 16) & 0xFF), 255);
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);
}
