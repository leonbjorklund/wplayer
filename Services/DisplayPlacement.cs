using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WPlayer.Services;

internal enum DisplayDirection
{
    Left,
    Right,
    Up,
    Down
}

internal sealed record DisplayMonitor(
    nint Handle,
    string Id,
    string DeviceName,
    PixelRect Bounds,
    bool IsPrimary,
    uint Dpi);

internal sealed record DisplayMonitorOption(
    string Id,
    int? Number,
    string ModelName,
    bool IsPrimary,
    bool IsAvailable = true)
{
    public string Label => DisplayMonitorOptions.FormatLabel(ModelName, Number, IsAvailable);
}

internal static class DisplayMonitorOptions
{
    private const string DevicePrefix = @"\\.\DISPLAY";

    public static int GetNumber(string deviceName, int fallback)
    {
        var suffix = deviceName.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase)
            ? deviceName[DevicePrefix.Length..]
            : "";
        return int.TryParse(suffix, out var number) && number > 0 ? number : fallback;
    }

    public static string FormatLabel(string? modelName, int? number, bool isAvailable = true)
    {
        var name = string.IsNullOrWhiteSpace(modelName) ? "Monitor" : modelName.Trim();
        var label = number is null ? name : $"({number}) {name}";
        return isAvailable ? label : $"{label} — unavailable";
    }

    public static DisplayMonitorOption? ResolveSelected(
        string? monitorId,
        IReadOnlyList<DisplayMonitorOption> options)
    {
        return options.FirstOrDefault(option =>
                   string.Equals(option.Id, monitorId, StringComparison.OrdinalIgnoreCase))
               ?? options.FirstOrDefault(option => option.IsPrimary && option.IsAvailable)
               ?? options.FirstOrDefault(option => option.IsAvailable)
               ?? options.FirstOrDefault();
    }

    public static DisplayMonitorOption[] IncludePreferred(
        IEnumerable<DisplayMonitorOption> activeOptions,
        string? preferredMonitorId,
        DisplayMonitorOption? knownPreferred)
    {
        var options = activeOptions.ToList();
        if (!string.IsNullOrWhiteSpace(preferredMonitorId)
            && options.All(option =>
                !string.Equals(option.Id, preferredMonitorId, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(knownPreferred is null
                ? new DisplayMonitorOption(preferredMonitorId, null, "Monitor", false, false)
                : knownPreferred with { IsAvailable = false });
        }

        return options
            .OrderBy(option => option.Number ?? int.MaxValue)
            .ThenBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

internal readonly record struct DisplayPlacementResult(
    DisplayMonitor Monitor,
    PixelRect WindowRect,
    double X,
    double Y,
    double Width,
    bool IsFallback = false);

internal static class DisplayPlacementMath
{
    public static DisplayMonitor? FindMonitor(string? monitorId, IEnumerable<DisplayMonitor> monitors) =>
        string.IsNullOrWhiteSpace(monitorId)
            ? null
            : monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.Id, monitorId, StringComparison.OrdinalIgnoreCase));

    public static DisplayMonitor ResolveMonitor(
        string? monitorId,
        IEnumerable<DisplayMonitor> monitors,
        out bool missing)
    {
        var available = monitors.ToList();
        var primary = available.FirstOrDefault(monitor => monitor.IsPrimary) ??
            available.FirstOrDefault() ??
            throw new InvalidOperationException("No display monitor is available.");
        var selected = FindMonitor(monitorId, available);
        missing = !string.IsNullOrWhiteSpace(monitorId) && selected is null;
        return selected ?? primary;
    }

    public static DisplayMonitor? FindDirectional(
        DisplayMonitor current,
        IEnumerable<DisplayMonitor> monitors,
        DisplayDirection direction) =>
        monitors
            .Where(candidate => candidate != current && IsInDirection(current.Bounds, candidate.Bounds, direction))
            .OrderBy(candidate => DistanceSquared(current.Bounds, candidate.Bounds))
            .FirstOrDefault();

    public static DisplayPlacementResult Place(
        DisplayMonitor monitor,
        double x,
        double y,
        double width,
        PixelRect content)
    {
        var scale = monitor.Dpi / 96.0;
        var bounds = monitor.Bounds;
        var fittedWidth = FitWidth(width, bounds.Width, scale);
        var pixelWidth = Math.Min(bounds.Width, Math.Max(1, content.Width));
        var pixelHeight = Math.Min(bounds.Height, Math.Max(1, content.Height));
        var left = bounds.Left + ToPixels(Math.Max(0, x), scale);
        var bottom = bounds.Bottom - ToPixels(Math.Max(0, y), scale);
        var rect = Clamp(new PixelRect(left, bottom - pixelHeight, left + pixelWidth, bottom), bounds);

        return new DisplayPlacementResult(
            monitor,
            rect,
            (rect.Left - bounds.Left) / scale,
            (bounds.Bottom - rect.Bottom) / scale,
            fittedWidth);
    }

    public static DisplayPlacementResult Capture(
        DisplayMonitor monitor,
        PixelRect window,
        double configuredWidth)
    {
        var scale = monitor.Dpi / 96.0;
        var bounds = monitor.Bounds;
        var width = FitWidth(configuredWidth, bounds.Width, scale);
        var pixelWidth = Math.Min(bounds.Width, Math.Max(1, ToPixels(width, scale)));
        var pixelHeight = Math.Min(bounds.Height, Math.Max(1, window.Height));
        var clamped = Clamp(
            new PixelRect(window.Left, window.Top, window.Left + pixelWidth, window.Top + pixelHeight),
            bounds);

        return new DisplayPlacementResult(
            monitor,
            clamped,
            (clamped.Left - bounds.Left) / scale,
            (bounds.Bottom - clamped.Bottom) / scale,
            width);
    }

    internal static PixelRect Clamp(PixelRect window, PixelRect workArea)
    {
        var width = Math.Min(window.Width, workArea.Width);
        var height = Math.Min(window.Height, workArea.Height);
        var left = Math.Clamp(window.Left, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(window.Top, workArea.Top, workArea.Bottom - height);
        return new PixelRect(left, top, left + width, top + height);
    }

    internal static uint ScalePercentToDpi(int percent) =>
        (uint)Math.Round(percent * 96 / 100d);

    private static double FitWidth(double width, int pixelWidth, double scale) =>
        Math.Min(width, Math.Floor(pixelWidth / scale));

    private static double DistanceSquared(PixelRect first, PixelRect second)
    {
        var x = first.CenterX - second.CenterX;
        var y = first.CenterY - second.CenterY;
        return x * x + y * y;
    }

    private static bool IsInDirection(PixelRect current, PixelRect candidate, DisplayDirection direction) =>
        direction switch
        {
            DisplayDirection.Left => candidate.CenterX < current.CenterX,
            DisplayDirection.Right => candidate.CenterX > current.CenterX,
            DisplayDirection.Up => candidate.CenterY < current.CenterY,
            DisplayDirection.Down => candidate.CenterY > current.CenterY,
            _ => false
        };

    private static int ToPixels(double value, double scale) =>
        (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
}

internal sealed class DisplayPlacementService : IDisposable
{
    private const int MonitorDefaultToNearest = 2;
    private const int MonitorInfoPrimary = 1;
    private const uint EddGetDeviceInterfaceName = 1;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private const int WmPowerBroadcast = 0x0218;
    private const int WmSettingChange = 0x001A;
    private const int PbtApmSuspend = 0x0004;
    private const int PbtApmResumeAutomatic = 0x0012;

    private readonly Action _displayChanged;
    private readonly Action _suspending;
    private readonly Action _resumed;
    private readonly FrameworkElement _content;
    private nint _hwnd;
    private HwndSource? _source;

    public DisplayPlacementService(
        Window window,
        FrameworkElement content,
        Action displayChanged,
        Action suspending,
        Action resumed)
    {
        _displayChanged = displayChanged;
        _suspending = suspending;
        _resumed = resumed;
        _content = content;
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    public void Dispose()
    {
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    public bool IsMonitorAvailable(string? monitorId) =>
        DisplayPlacementMath.FindMonitor(monitorId, GetMonitors()) is not null;

    public Task<DisplayMonitorOption[]> GetMonitorOptionsAsync() =>
        Task.WhenAll(GetMonitors().Select(CreateMonitorOptionAsync));

    public DisplayPlacementResult Place(AppConfig config)
    {
        var window = WindowPlacement.GetWindowRect(_hwnd);
        var monitor = DisplayPlacementMath.ResolveMonitor(config.MonitorId, GetMonitors(), out var missing);
        var x = missing ? AppConfig.DefaultX : config.X;
        var y = missing ? AppConfig.DefaultY : config.Y;
        var content = GetContentRect();
        var result = DisplayPlacementMath.Place(monitor, x, y, config.Width, content);
        WindowPlacement.SetWindowPosition(
            _hwnd,
            Translate(window, result.WindowRect.Left - content.Left, result.WindowRect.Top - content.Top));
        return result with { IsFallback = missing };
    }

    public DisplayPlacementResult Capture(AppConfig config)
    {
        var rect = WindowPlacement.GetWindowRect(_hwnd);
        var monitors = GetMonitors();
        var handle = MonitorFromWindow(_hwnd, MonitorDefaultToNearest);
        var monitor = monitors.FirstOrDefault(candidate => candidate.Handle == handle) ??
            throw new InvalidOperationException("Could not find the player monitor.");
        var content = GetContentRect();
        var result = DisplayPlacementMath.Capture(monitor, content, config.Width);
        WindowPlacement.SetWindowPosition(
            _hwnd,
            Translate(rect, result.WindowRect.Left - content.Left, result.WindowRect.Top - content.Top));
        return result;
    }

    public DisplayMonitor? FindDirectional(AppConfig config, DisplayDirection direction)
    {
        var monitors = GetMonitors();
        var current = monitors.FirstOrDefault(monitor =>
            string.Equals(monitor.Id, config.MonitorId, StringComparison.OrdinalIgnoreCase)) ??
            monitors.FirstOrDefault(monitor => monitor.Handle == MonitorFromWindow(_hwnd, MonitorDefaultToNearest));
        return current is null ? null : DisplayPlacementMath.FindDirectional(current, monitors, direction);
    }

    internal static IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var monitors = new List<DisplayMonitor>();
        MonitorEnumProc callback = (nint handle, nint hdc, ref PixelRect rect, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(handle, ref info))
            {
                return true;
            }

            var deviceName = info.DeviceName ?? string.Empty;
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            var id = EnumDisplayDevices(deviceName, 0, ref device, EddGetDeviceInterfaceName)
                && !string.IsNullOrWhiteSpace(device.DeviceId)
                ? device.DeviceId
                : deviceName;
            var dpi = GetScaleFactorForMonitor(handle, out var scale) == 0
                ? DisplayPlacementMath.ScalePercentToDpi(scale)
                : 96u;
            monitors.Add(new DisplayMonitor(
                handle,
                id,
                deviceName,
                info.Monitor,
                (info.Flags & MonitorInfoPrimary) != 0,
                dpi));
            return true;
        };
        EnumDisplayMonitors(0, 0, callback, 0);
        return monitors;
    }

    private static async Task<DisplayMonitorOption> CreateMonitorOptionAsync(
        DisplayMonitor monitor,
        int fallbackNumber)
    {
        string? modelName = null;
        try
        {
            var display = await Windows.Devices.Display.DisplayMonitor.FromInterfaceIdAsync(monitor.Id);
            modelName = display?.DisplayName;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not read monitor name for {monitor.DeviceName}", ex);
        }

        return new DisplayMonitorOption(
            monitor.Id,
            DisplayMonitorOptions.GetNumber(monitor.DeviceName, fallbackNumber + 1),
            string.IsNullOrWhiteSpace(modelName) ? "Monitor" : modelName.Trim(),
            monitor.IsPrimary);
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmPowerBroadcast)
        {
            if ((int)wParam == PbtApmSuspend)
            {
                _suspending();
                handled = true;
                return 1;
            }

            if ((int)wParam == PbtApmResumeAutomatic)
            {
                _resumed();
                handled = true;
                return 1;
            }
        }

        if (message is WmDisplayChange or WmDpiChanged or WmSettingChange)
        {
            _displayChanged();
        }

        return 0;
    }

    private PixelRect GetContentRect()
    {
        var topLeft = _content.PointToScreen(new Point());
        var bottomRight = _content.PointToScreen(new Point(_content.ActualWidth, _content.ActualHeight));
        return new PixelRect(
            (int)Math.Round(topLeft.X),
            (int)Math.Round(topLeft.Y),
            (int)Math.Round(bottomRight.X),
            (int)Math.Round(bottomRight.Y));
    }

    private static PixelRect Translate(PixelRect rect, int x, int y) =>
        new(rect.Left + x, rect.Top + y, rect.Right + x, rect.Bottom + y);

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, ref PixelRect rect, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string deviceName,
        uint deviceNumber,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetScaleFactorForMonitor(nint monitor, out int scale);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public PixelRect Monitor;
        public PixelRect Work;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
}
