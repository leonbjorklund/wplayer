using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WPlayer.Services;

internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public double CenterX => Left + Width / 2.0;
    public double CenterY => Top + Height / 2.0;
}

internal static class WindowPlacementMath
{
    internal static PixelRect Center(PixelRect window, PixelRect workArea)
    {
        var left = window.Width >= workArea.Width
            ? workArea.Left
            : workArea.Left + (workArea.Width - window.Width) / 2;
        var top = window.Height >= workArea.Height
            ? workArea.Top
            : workArea.Top + (workArea.Height - window.Height) / 2;
        return new PixelRect(left, top, left + window.Width, top + window.Height);
    }
}

internal static class WindowPlacement
{
    private const int MonitorDefaultToPrimary = 1;
    private const int MonitorDefaultToNearest = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoZOrder = 0x0004;

    internal static void CenterOnCursorMonitor(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == 0)
            {
                throw new InvalidOperationException("Could not find the window.");
            }

            var windowRect = GetWindowRect(hwnd);
            var monitor = GetCursorPos(out var cursor)
                ? MonitorFromPoint(cursor, MonitorDefaultToNearest)
                : 0;
            monitor = monitor != 0
                ? monitor
                : MonitorFromPoint(default, MonitorDefaultToPrimary);

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            {
                throw new InvalidOperationException("Could not find the cursor monitor.");
            }

            SetWindowPosition(hwnd, WindowPlacementMath.Center(windowRect, info.Work));
            var dpiAdjustedRect = GetWindowRect(hwnd);
            SetWindowPosition(hwnd, WindowPlacementMath.Center(dpiAdjustedRect, info.Work));
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not center window on cursor monitor", ex);
        }
    }

    internal static PixelRect GetWindowRect(nint hwnd)
    {
        if (!GetWindowRectNative(hwnd, out var rect))
        {
            throw new InvalidOperationException("Could not read the window position.");
        }

        return rect;
    }

    internal static bool IsNativeVisible(Window window) =>
        IsWindowVisible(new WindowInteropHelper(window).Handle);

    internal static void SetWindowPosition(nint hwnd, PixelRect rect)
    {
        if (!SetWindowPos(
                hwnd,
                0,
                rect.Left,
                rect.Top,
                0,
                0,
                SwpNoActivate | SwpNoSize | SwpNoOwnerZOrder | SwpNoZOrder))
        {
            throw new InvalidOperationException("Could not place the window.");
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PixelPoint point);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRectNative(nint hwnd, out PixelRect rect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(PixelPoint point, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PixelPoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public PixelRect Monitor;
        public PixelRect Work;
        public int Flags;
    }
}
