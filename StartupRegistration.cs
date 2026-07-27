using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace WPlayer;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WPlayer";
    private const string StoreTaskId = "WPlayerStartup";

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (PackageIdentity.IsPackaged)
            {
                return SetPackagedEnabled(enabled);
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            return SetEnabled(key, enabled, Environment.ProcessPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or COMException or InvalidOperationException)
        {
            AppLog.Error("Could not update startup registration", ex);
            return false;
        }
    }

    private static bool SetPackagedEnabled(bool enabled)
    {
        var task = StartupTask.GetAsync(StoreTaskId).AsTask().GetAwaiter().GetResult();
        if (!enabled)
        {
            task.Disable();
            return true;
        }

        if (task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
        {
            return true;
        }

        var state = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
        return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    internal static bool SetEnabled(RegistryKey? key, bool enabled, string? executablePath)
    {
        if (key is null)
        {
            return false;
        }

        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return true;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(executablePath);
        if (!File.Exists(executablePath)
            || string.IsNullOrWhiteSpace(directory)
            || !string.Equals(Path.GetFileName(directory), "current", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(directory, "sq.version")))
        {
            return false;
        }

        key.SetValue(ValueName, $"\"{executablePath}\"");
        return true;
    }

}
