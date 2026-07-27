using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace WPlayer;

public enum VolumeScrollTarget
{
    CurrentApp,
    WindowsMaster
}

public sealed class AppConfig
{
    public const double DefaultWidth = 360;
    public const double DefaultX = 2;
    public const double DefaultY = 2;
    public const double DefaultPlayerScale = 1;
    public const string DefaultBackgroundColor = "#1C1C1C";
    public const string DefaultBorderColor = "Transparent";
    public const string DefaultTextColor = "#F1F3F4";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<VolumeScrollTarget>(JsonNamingPolicy.CamelCase) }
    };

    public List<MediaAppConfig> MediaApps { get; set; } = [];
    public double Width { get; set; } = DefaultWidth;
    public string? MonitorId { get; set; }
    public double X { get; set; } = DefaultX;
    public double Y { get; set; } = DefaultY;
    public double PlayerScale { get; set; } = DefaultPlayerScale;
    public bool ShowPreviousButton { get; set; } = true;
    public bool ShowPlayPauseButton { get; set; } = true;
    public bool ShowNextButton { get; set; } = true;
    public bool ShowIcon { get; set; } = true;
    public bool ShowIconOnlyWithMultipleSources { get; set; }
    public bool DragToMove { get; set; } = true;
    public bool LaunchAtStartup { get; set; } = true;
    public VolumeScrollTarget ScrollVolumeTarget { get; set; } = VolumeScrollTarget.CurrentApp;
    public string BackgroundColor { get; set; } = DefaultBackgroundColor;
    public string BorderColor { get; set; } = DefaultBorderColor;
    public string TextColor { get; set; } = DefaultTextColor;

    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WPlayer",
        "config.json");

    public static AppConfig Load() => Load(ConfigPath);

    internal static void DeleteLocalData() => DeleteLocalData(Path.GetDirectoryName(ConfigPath)!);

    internal static void DeleteLocalData(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    internal static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var defaults = new AppConfig();
            defaults.Save(path);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = Deserialize(json);
            config.Save(path);
            return config;
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not load configuration", ex);
            BackupBadConfig(path);
            var defaults = new AppConfig();
            defaults.Save(path);
            return defaults;
        }
    }

    public bool Save() => Save(ConfigPath);

    internal bool Save(string path)
    {
        try
        {
            Normalize();
            WriteAllTextAtomic(path, Serialize(this));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not save configuration", ex);
            return false;
        }
    }

    internal void Normalize()
    {
        MediaApps = NormalizeMediaApps(MediaApps);
        PlayerScale = NormalizePlayerScale(PlayerScale);
        Width = ClampFinite(Width, DefaultWidth, 220, double.MaxValue);
        MonitorId = string.IsNullOrWhiteSpace(MonitorId) ? null : MonitorId.Trim();
        X = FiniteOrDefault(X, DefaultX);
        Y = FiniteOrDefault(Y, DefaultY);
        ScrollVolumeTarget = Enum.IsDefined(ScrollVolumeTarget)
            ? ScrollVolumeTarget
            : VolumeScrollTarget.CurrentApp;
        BackgroundColor = NormalizeColor(BackgroundColor, DefaultBackgroundColor);
        BorderColor = NormalizeColor(BorderColor, DefaultBorderColor);
        TextColor = NormalizeColor(TextColor, DefaultTextColor);
    }

    private static List<MediaAppConfig> NormalizeMediaApps(IEnumerable<MediaAppConfig?>? apps)
    {
        var normalized = new List<MediaAppConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps ?? [])
        {
            var sourceAppUserModelId = app?.SourceAppUserModelId?.Trim();
            if (string.IsNullOrWhiteSpace(sourceAppUserModelId) || !seen.Add(sourceAppUserModelId))
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(app!.DisplayName)
                ? sourceAppUserModelId
                : app.DisplayName.Trim();
            normalized.Add(new MediaAppConfig(sourceAppUserModelId, displayName, app.Enabled));
        }

        return normalized;
    }

    internal static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, JsonOptions);

    internal static AppConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();

    private static double ClampFinite(double value, double fallback, double min, double max) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, min, max);

    private static double FiniteOrDefault(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    private static double NormalizePlayerScale(double value) =>
        Math.Round(ClampFinite(value, DefaultPlayerScale, 1, 2) * 20, MidpointRounding.AwayFromZero) / 20;

    private static string NormalizeColor(string? value, string fallback)
    {
        var color = value?.Trim();
        if (string.IsNullOrWhiteSpace(color))
        {
            return fallback;
        }

        try
        {
            _ = ColorConverter.ConvertFromString(color);
            return color;
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static void BackupBadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var directory = Path.GetDirectoryName(path)!;
            var backupPath = Path.Combine(directory, $"config.bad-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            File.Copy(path, backupPath, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Could not back up invalid configuration", ex);
        }
    }

    private static void WriteAllTextAtomic(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, contents);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
                return;
            }

            File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public bool EnsureMediaApp(string sourceAppUserModelId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return false;
        }

        var existing = MediaApps.FirstOrDefault(app =>
            string.Equals(app.SourceAppUserModelId, sourceAppUserModelId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            MediaApps.Add(new MediaAppConfig(sourceAppUserModelId, displayName, true));
            return true;
        }

        if (string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal))
        {
            return false;
        }

        existing.DisplayName = displayName;
        return true;
    }

    public bool IsMediaAppEnabled(string sourceAppUserModelId) =>
        MediaApps.FirstOrDefault(item =>
            string.Equals(item.SourceAppUserModelId, sourceAppUserModelId, StringComparison.OrdinalIgnoreCase))?.Enabled == true;
}

public sealed class MediaAppConfig(string sourceAppUserModelId, string displayName, bool enabled)
{
    public string SourceAppUserModelId { get; set; } = sourceAppUserModelId;
    public string DisplayName { get; set; } = displayName;
    public bool Enabled { get; set; } = enabled;
}
