using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace WPlayer.Services;

public static class WindowZOrder
{
    private static readonly nint HwndTopmost = new(-1);
    private const int GwlExStyle = -20;
    private const uint GwHwndPrev = 3;
    private const uint MonitorDefaultToNull = 0;
    private const int MaxZOrderScan = 128;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExAppWindow = 0x00040000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectReorder = 0x8004;
    private const uint WineventSkipOwnProcess = 0x0002;

    public static void ApplyOverlayStyle(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return;
        }

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        // Hide from taskbar/Alt-Tab natively; WPF ShowInTaskbar=false creates a hidden owner that breaks topmost overlays.
        exStyle |= WsExToolWindow;
        exStyle &= ~WsExAppWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, new nint(exStyle));
        SetTopmost(hwnd, SwpFrameChanged);
    }

    public static IDisposable? WatchShellTopmostChanges(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        // Win+D can move the taskbar above topmost windows; shell z-order events are the reliable point to reinsert.
        return hwnd == 0 ? null : new ShellTopmostWatcher(hwnd, window.Dispatcher);
    }

    private static void SetTopmost(nint hwnd, uint extraFlags = 0)
    {
        SetWindowPos(
            hwnd,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | extraFlags);
    }

    private static bool IsTaskbarAboveOnSameMonitor(nint hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNull);
        if (monitor == 0)
        {
            return false;
        }

        var candidate = hwnd;
        for (var i = 0; i < MaxZOrderScan; i++)
        {
            candidate = GetWindow(candidate, GwHwndPrev);
            if (candidate == 0 || candidate == hwnd)
            {
                return false;
            }

            if (MonitorFromWindow(candidate, MonitorDefaultToNull) != monitor)
            {
                continue;
            }

            var className = new StringBuilder(32);
            if (GetClassName(candidate, className, className.Capacity) > 0 &&
                className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return true;
            }
        }

        return false;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventProcModule,
        WinEventProc eventProc,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    private delegate void WinEventProc(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);

    private sealed class ShellTopmostWatcher : IDisposable
    {
        private readonly nint _hwnd;
        private readonly Dispatcher _dispatcher;
        private readonly WinEventProc _callback;
        private readonly nint[] _hooks;
        private int _disposed;
        private int _reassertPending;

        public ShellTopmostWatcher(nint hwnd, Dispatcher dispatcher)
        {
            _hwnd = hwnd;
            _dispatcher = dispatcher;
            _callback = OnShellEvent;
            _hooks =
            [
                SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, _callback, 0, 0, WineventSkipOwnProcess),
                SetWinEventHook(EventSystemMinimizeStart, EventSystemMinimizeEnd, 0, _callback, 0, 0, WineventSkipOwnProcess),
                SetWinEventHook(EventObjectReorder, EventObjectReorder, 0, _callback, 0, 0, WineventSkipOwnProcess)
            ];
            Reassert();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            foreach (var hook in _hooks)
            {
                if (hook != 0)
                {
                    UnhookWinEvent(hook);
                }
            }

        }

        private void OnShellEvent(
            nint hook,
            uint eventType,
            nint hwnd,
            int idObject,
            int idChild,
            uint eventThread,
            uint eventTime)
        {
            QueueReassert();
        }

        private void QueueReassert()
        {
            if (_disposed == 0 && Interlocked.CompareExchange(ref _reassertPending, 1, 0) == 0)
            {
                _dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        Reassert();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _reassertPending, 0);
                    }
                }, DispatcherPriority.Send);
            }
        }

        private void Reassert()
        {
            if (_disposed == 0 && IsTaskbarAboveOnSameMonitor(_hwnd))
            {
                SetTopmost(_hwnd);
            }
        }
    }
}
