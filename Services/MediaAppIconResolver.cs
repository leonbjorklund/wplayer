using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WPlayer.Services;

public static class MediaAppIconResolver
{
    private static readonly Dictionary<string, BitmapSource> Icons = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource? GetIcon(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return null;
        }

        var key = sourceAppUserModelId.Trim();
        if (Icons.TryGetValue(key, out var cachedIcon))
        {
            return cachedIcon;
        }

        foreach (var process in Process.GetProcessesByName(MediaProcess.GetProcessName(key)))
        {
            using (process)
            {
                var path = MediaProcess.TryGetPath(process);
                if (path is null)
                {
                    continue;
                }

                var icon = TryExtractIcon(path);
                if (icon is null)
                {
                    continue;
                }

                Icons[key] = icon;
                return icon;
            }
        }

        return null;
    }

    private static BitmapSource? TryExtractIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
    }

}
