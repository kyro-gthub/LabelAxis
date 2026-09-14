using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LabelMaker.App.Services;

/// <summary>
/// Keeps a <c>WindowStyle="None"</c> + <c>WindowChrome</c> window on screen.
///
/// Windows maximises a resizable window to its monitor's work area grown by the resize
/// frame, so part of the window hangs off every edge; with the frame drawn away by
/// WindowChrome that overhang cuts off the title bar buttons and the status bar. Forcing
/// the maximised size through WM_GETMINMAXINFO fixed it on one monitor but not on PCs
/// whose monitors differ in size or scaling, because Windows rescales that size between
/// monitors. Instead the window is left to maximise normally and its content is inset by
/// however far the window really extends past the work area of the monitor it is on,
/// measured after every size or move, so it fits whatever the monitor layout, taskbar
/// position or DPI.
///
/// It also keeps the minimum size within the monitor, so a small or highly scaled laptop
/// screen never gets a window that is taller than the screen.
/// </summary>
public static class MaximiseFix
{
    private const int MonitorDefaultToNearest = 0x00000002;

    public static void Attach(Window window, FrameworkElement content)
    {
        double designMinWidth = window.MinWidth;
        double designMinHeight = window.MinHeight;

        void Update()
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !TryGetWorkArea(hwnd, out NativeRect work) || !GetWindowRect(hwnd, out NativeRect rect))
            {
                return;
            }

            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            double sx = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
            double sy = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;

            // Never ask for more than the monitor can show.
            window.MinWidth = Math.Min(designMinWidth, Math.Max(320, (work.Right - work.Left) / sx));
            window.MinHeight = Math.Min(designMinHeight, Math.Max(240, (work.Bottom - work.Top) / sy));

            if (window.WindowState != WindowState.Maximized)
            {
                content.Margin = default;
                return;
            }

            content.Margin = new Thickness(
                Math.Max(0, work.Left - rect.Left) / sx,
                Math.Max(0, work.Top - rect.Top) / sy,
                Math.Max(0, rect.Right - work.Right) / sx,
                Math.Max(0, rect.Bottom - work.Bottom) / sy);
        }

        window.SourceInitialized += (_, _) =>
        {
            FitToMonitor(window);
            Update();
        };
        window.StateChanged += (_, _) => Update();
        window.SizeChanged += (_, _) => Update();
        window.LocationChanged += (_, _) => Update();
        window.DpiChanged += (_, _) => Update();
    }

    /// <summary>A first window bigger than the screen starts sized to 92% of the work area, centred.</summary>
    private static void FitToMonitor(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (!TryGetWorkArea(hwnd, out NativeRect work))
        {
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        double workW = (work.Right - work.Left) / dpi.DpiScaleX;
        double workH = (work.Bottom - work.Top) / dpi.DpiScaleY;
        if (window.Width <= workW && window.Height <= workH)
        {
            return;
        }

        window.Width = Math.Min(window.Width, workW * 0.92);
        window.Height = Math.Min(window.Height, workH * 0.92);
        window.Left = (work.Left / dpi.DpiScaleX) + ((workW - window.Width) / 2);
        window.Top = (work.Top / dpi.DpiScaleY) + ((workH - window.Height) / 2);
    }

    private static bool TryGetWorkArea(IntPtr hwnd, out NativeRect work)
    {
        work = default;
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        work = info.Work;
        return work.Right > work.Left && work.Bottom > work.Top;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }
}
