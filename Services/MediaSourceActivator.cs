using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WPlayer.Services;

public static class MediaSourceActivator
{
    private const int SwRestore = 9;

    public static void Open(string sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return;
        }

        try
        {
            if (TryActivateRunningProcess(sourceAppUserModelId, out var runningProcessPath)
                || TryActivateApplication(sourceAppUserModelId)
                || TryStart(runningProcessPath ?? sourceAppUserModelId))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not focus media source", ex);
        }
    }

    private static bool TryActivateRunningProcess(string sourceAppUserModelId, out string? executablePath)
    {
        executablePath = null;
        var processName = MediaProcess.GetProcessName(sourceAppUserModelId);

        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                executablePath ??= MediaProcess.TryGetPath(process);
                var hwnd = process.MainWindowHandle;
                if (hwnd == 0)
                {
                    continue;
                }

                if (IsIconic(hwnd))
                {
                    ShowWindow(hwnd, SwRestore);
                }

                return SetForegroundWindow(hwnd);
            }
        }

        return false;
    }

    private static bool TryActivateApplication(string sourceAppUserModelId)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ApplicationActivationManagerClsid, true)!;
            var manager = (IApplicationActivationManager)Activator.CreateInstance(type)!;
            var result = manager.ActivateApplication(sourceAppUserModelId, null, 0, out _);
            Debug.WriteLineIf(result != 0, $"ActivateApplication failed for {sourceAppUserModelId}: 0x{result:X8}");
            return result == 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not activate media application", ex);
            return false;
        }
    }

    private static bool TryStart(string sourceAppUserModelId)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(sourceAppUserModelId) { UseShellExecute = true });
            return process is not null;
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not start media source", ex);
            return false;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    private static readonly Guid ApplicationActivationManagerClsid = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            int options,
            out uint processId);
    }
}
