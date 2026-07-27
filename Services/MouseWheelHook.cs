using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WPlayer.Services;

internal sealed class MouseWheelHook : IDisposable
{
    private const int WhMouseLowLevel = 14;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmQuit = 0x0012;
    private const uint GaRoot = 2;
    private readonly nint _owner;
    private readonly Func<bool> _isTargetHovered;
    private readonly Action<int> _onWheel;
    private readonly HookProc _callback;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private Exception? _startupError;
    private nint _hook;
    private uint _threadId;
    private int _disposed;

    public MouseWheelHook(nint owner, Func<bool> isTargetHovered, Action<int> onWheel)
    {
        _owner = owner;
        _isTargetHovered = isTargetHovered;
        _onWheel = onWheel;
        _callback = OnMouse;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "WPlayer mouse wheel"
        };
        _thread.Start();
        _ready.Wait();

        if (_startupError is not null)
        {
            Dispose();
            throw new InvalidOperationException("Could not start mouse wheel handling", _startupError);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        PostThreadMessage(_threadId, WmQuit, 0, 0);
        _thread.Join();
        _ready.Dispose();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, 0);
        _hook = SetWindowsHookEx(WhMouseLowLevel, _callback, GetModuleHandle(null), 0);
        if (_hook == 0)
        {
            _startupError = new Win32Exception(Marshal.GetLastWin32Error());
            _ready.Set();
            return;
        }

        _ready.Set();
        try
        {
            while (GetMessage(out _, 0, 0, 0) > 0) { }
        }
        finally
        {
            UnhookWindowsHookEx(_hook);
        }
    }

    private nint OnMouse(int code, nuint message, nint data)
    {
        if (code >= 0 && message == WmMouseWheel)
        {
            var input = Marshal.PtrToStructure<LowLevelMouseInput>(data);
            var window = WindowFromPoint(input.Point);
            if (_isTargetHovered() && GetAncestor(window, GaRoot) == _owner)
            {
                _onWheel(unchecked((short)(input.MouseData >> 16)));
                return 1;
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    private delegate nint HookProc(int code, nuint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct LowLevelMouseInput(
        NativePoint Point,
        uint MouseData,
        uint Flags,
        uint Time,
        nuint ExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeMessage(
        nint Window,
        uint Message,
        nuint WParam,
        nint LParam,
        uint Time,
        NativePoint Point,
        uint Private);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookType, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, nint window, uint min, uint max, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
