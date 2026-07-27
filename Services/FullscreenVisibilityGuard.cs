using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WPlayer.Services;

internal readonly record struct FullscreenWindowObservation(
    PixelRect VisibleBounds,
    PixelRect MonitorBounds,
    uint ShowCommand,
    uint Style);

internal static class FullscreenVisibilityPolicy
{
    internal const uint ShowMaximized = 3;
    internal const uint StyleCaption = 0x00C00000;
    internal const uint StyleThickFrame = 0x00040000;
    private const int FullscreenTolerance = 2;

    internal static bool ShouldHide(FullscreenWindowObservation observation)
    {
        if (!Covers(observation.VisibleBounds, observation.MonitorBounds))
        {
            return false;
        }

        var hasStandardFrame = (observation.Style & (StyleCaption | StyleThickFrame)) != 0;
        return observation.ShowCommand != ShowMaximized || !hasStandardFrame;
    }

    private static bool Covers(PixelRect window, PixelRect monitor) =>
        window.Left <= monitor.Left + FullscreenTolerance &&
        window.Top <= monitor.Top + FullscreenTolerance &&
        window.Right >= monitor.Right - FullscreenTolerance &&
        window.Bottom >= monitor.Bottom - FullscreenTolerance;
}

public sealed class FullscreenVisibilityGuard : IDisposable
{
    private const int DwmExtendedFrameBounds = 9;
    private const int GwlStyle = -16;
    private const int MonitorDefaultToNearest = 2;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private readonly Window _window;
    private readonly nint _hwnd;
    private readonly int _processId;
    private readonly DispatcherTimer _timer;
    private bool _hiddenForFullscreen;

    public FullscreenVisibilityGuard(Window window)
    {
        _window = window;
        _hwnd = new WindowInteropHelper(window).Handle;
        _processId = Environment.ProcessId;
        _timer = new DispatcherTimer(DispatcherPriority.Send, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        Update();
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    private void Update()
    {
        if (_hwnd == 0)
        {
            return;
        }

        var shouldHide = ForegroundCoversWindowMonitor();
        if (shouldHide == _hiddenForFullscreen)
        {
            return;
        }

        _hiddenForFullscreen = shouldHide;
        if (shouldHide)
        {
            ShowWindow(_hwnd, SwHide);
            return;
        }

        ShowWindow(_hwnd, SwShowNoActivate);
        WindowZOrder.ApplyOverlayStyle(_window);
    }

    private bool ForegroundCoversWindowMonitor()
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == _hwnd || IsIconic(foreground))
        {
            return false;
        }

        if (IsShellDesktopWindow(foreground))
        {
            return false;
        }

        if (GetWindowThreadProcessId(foreground, out var processId) == 0)
        {
            return false;
        }

        if (processId == _processId)
        {
            return false;
        }

        var windowMonitor = MonitorFromWindow(_hwnd, MonitorDefaultToNearest);
        var foregroundMonitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (windowMonitor == 0 || foregroundMonitor == 0 || windowMonitor != foregroundMonitor)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!TryGetVisibleWindowRect(foreground, out var foregroundRect) ||
            !GetMonitorInfo(windowMonitor, ref monitorInfo) ||
            !TryGetWindowShowCommand(foreground, out var showCommand) ||
            !TryGetWindowStyle(foreground, out var style))
        {
            return false;
        }

        return FullscreenVisibilityPolicy.ShouldHide(new(
            foregroundRect.ToPixelRect(),
            monitorInfo.Monitor.ToPixelRect(),
            showCommand,
            style));
    }

    private static bool TryGetVisibleWindowRect(nint hwnd, out Rect rect)
    {
        if (DwmGetWindowAttribute(
                hwnd,
                DwmExtendedFrameBounds,
                out rect,
                Marshal.SizeOf<Rect>()) >= 0)
        {
            return true;
        }

        return GetWindowRect(hwnd, out rect);
    }

    private static bool TryGetWindowShowCommand(nint hwnd, out uint showCommand)
    {
        var placement = new WindowPlacement
        {
            Length = (uint)Marshal.SizeOf<WindowPlacement>()
        };
        if (!GetWindowPlacement(hwnd, ref placement))
        {
            showCommand = 0;
            return false;
        }

        showCommand = placement.ShowCommand;
        return true;
    }

    private static bool TryGetWindowStyle(nint hwnd, out uint style)
    {
        Marshal.SetLastPInvokeError(0);
        var value = GetWindowLongPtr(hwnd, GwlStyle);
        if (value == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            style = 0;
            return false;
        }

        style = unchecked((uint)value.ToInt64());
        return true;
    }

    private static bool IsShellDesktopWindow(nint hwnd)
    {
        var className = new StringBuilder(256);
        if (GetClassName(hwnd, className, className.Capacity) == 0)
        {
            return false;
        }

        return className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint hwnd,
        int dwAttribute,
        out Rect pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint hWnd, ref WindowPlacement lpwndpl);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCommand;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }
}
