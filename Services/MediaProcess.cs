using System.Diagnostics;
using System.IO;

namespace WPlayer.Services;

internal static class MediaProcess
{
    public static string? TryGetPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string GetProcessName(string sourceAppUserModelId)
    {
        var key = sourceAppUserModelId.Trim();
        var appSeparator = key.LastIndexOf('!');
        var appId = key[(appSeparator + 1)..];
        if (appId.Equals("App", StringComparison.OrdinalIgnoreCase) && appSeparator > 0)
        {
            var publisherSeparator = key.LastIndexOf('_', appSeparator);
            if (publisherSeparator > 0)
            {
                return key[..publisherSeparator];
            }
        }

        var name = Path.GetFileNameWithoutExtension(appId);
        return string.IsNullOrWhiteSpace(name) ? key : name;
    }
}
