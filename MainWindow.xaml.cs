using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WPlayer.Services;

namespace WPlayer;

public partial class MainWindow : Window
{
    private const double EmptyStateOpacity = 0.7;
    private const int VolumeStepPercent = 5;
    private const int ResumeMonitorCheckCount = 40;
    private static readonly TimeSpan ResumeMonitorCheckInterval = TimeSpan.FromMilliseconds(500);
    private readonly MediaSessionService _media = new();
    private readonly VolumeWheelAccumulator _volumeWheel = new();
    private readonly DispatcherTimer _resumeMonitorTimer;
    private readonly Dictionary<string, DisplayMonitorOption> _knownMonitorOptions =
        new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _topmostGuard;
    private MouseWheelHook? _mouseWheelHook;
    private FullscreenVisibilityGuard? _fullscreenGuard;
    private DisplayPlacementService? _displayPlacement;
    private SettingsWindow? _settingsWindow;
    private readonly AppConfig _config;
    private string? _currentSourceAppUserModelId;
    private MediaSnapshot? _renderedSnapshot;
    private string? _updateStatus;
    private string? _refreshErrorKey;
    private bool _refreshing;
    private bool _refreshQueued;
    private bool _dragging;
    private bool _positionQueued;
    private bool _hasMediaSession;
    private bool _volumeValuePinned;
    private bool _volumeScrollHovered;
    private string? _resumeMonitorId;
    private int _resumeMonitorChecksRemaining;
    private int _monitorOptionsRefreshVersion;
    private DisplayMonitorOption[] _activeMonitorOptions = [];

    internal MainWindow(AppConfig config)
    {
        InitializeComponent();
        _resumeMonitorTimer = new DispatcherTimer(DispatcherPriority.Loaded)
        {
            Interval = ResumeMonitorCheckInterval
        };
        _resumeMonitorTimer.Tick += (_, _) => OnResumeMonitorTimerTick();
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        SizeChanged += (_, _) => QueueApplyPosition();
        _config = config;
        ApplyConfig();
        _media.Changed += (_, _) => _ = Dispatcher.InvokeAsync(RefreshMediaAsync);

        var refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        refreshTimer.Tick += async (_, _) => await RefreshMediaAsync();
        refreshTimer.Start();

        SourceInitialized += (_, _) =>
        {
            _displayPlacement = new DisplayPlacementService(
                this,
                RootBorder,
                OnDisplayChanged,
                OnSuspending,
                OnResumed);
            ApplySavedPosition();
            _ = RefreshMonitorOptionsAsync();
            WindowZOrder.ApplyOverlayStyle(this);
            _topmostGuard = WindowZOrder.WatchShellTopmostChanges(this);
            _fullscreenGuard = new FullscreenVisibilityGuard(this);
            try
            {
                _mouseWheelHook = new MouseWheelHook(
                    new WindowInteropHelper(this).Handle,
                    () => Volatile.Read(ref _volumeScrollHovered),
                    delta => _ = Dispatcher.BeginInvoke(
                        () => AdjustVolume(delta),
                        DispatcherPriority.Input));
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not enable background mouse wheel handling", ex);
            }
        };
        Loaded += async (_, _) =>
        {
            ApplyConfig();
            ApplyContextMenuDpi();
            WindowZOrder.ApplyOverlayStyle(this);
            await RefreshMediaAsync();
            _ = AppUpdateService.CheckOnStartupAsync(status => _ = Dispatcher.InvokeAsync(() => SetUpdateStatus(status)));
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        _monitorOptionsRefreshVersion++;
        _resumeMonitorTimer.Stop();
        _mouseWheelHook?.Dispose();
        _fullscreenGuard?.Dispose();
        _displayPlacement?.Dispose();
        _topmostGuard?.Dispose();
        _media.Dispose();
        _settingsWindow?.Close();
        base.OnClosing(e);
    }

    private void ApplyConfig()
    {
        _config.Normalize();
        var metrics = new PlayerLayoutMetrics(_config.PlayerScale);
        ApplyPlayerLayout(metrics);
        NowPlayingText.FontSize = metrics.FontSize;
        ApplyNowPlayingLineHeight();
        PreviousButton.Visibility = _config.ShowPreviousButton ? Visibility.Visible : Visibility.Collapsed;
        PlayPauseButton.Visibility = _config.ShowPlayPauseButton ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = _config.ShowNextButton ? Visibility.Visible : Visibility.Collapsed;
        var highContrast = SystemParameters.HighContrast;
        var background = highContrast
            ? new SolidColorBrush(SystemColors.WindowColor)
            : BrushFromConfig(_config.BackgroundColor);
        var palette = ColorContrast.PaletteFromBackground(background.Color);
        if (!highContrast && background.Color.A == 0)
        {
            // Layered windows pass alpha-zero pixels through before WPF hit-testing.
            background.Color = Color.FromArgb(1, background.Color.R, background.Color.G, background.Color.B);
        }

        var border = highContrast
            ? new SolidColorBrush(SystemColors.WindowTextColor)
            : BrushFromConfig(_config.BorderColor);
        var text = highContrast
            ? new SolidColorBrush(SystemColors.WindowTextColor)
            : BrushFromConfig(_config.TextColor);
        var buttonBackground = new SolidColorBrush(highContrast
            ? SystemColors.ControlColor
            : palette.PlaybackBackground);
        var buttonForeground = new SolidColorBrush(highContrast
            ? SystemColors.ControlTextColor
            : palette.PlaybackForeground);

        Foreground = text;
        RootBorder.Background = background;
        RootBorder.BorderBrush = border;
        RootBorder.BorderThickness = highContrast || border.Color.A != 0
            ? new Thickness(1)
            : new Thickness(0);

        NowPlayingHitTarget.Foreground = text;
        Resources["PlaybackButtonHoverBackgroundBrush"] = new SolidColorBrush(highContrast
            ? SystemColors.HighlightColor
            : palette.PlaybackHoverBackground);
        Resources["PlaybackButtonHoverForegroundBrush"] = new SolidColorBrush(highContrast
            ? SystemColors.HighlightTextColor
            : palette.PlaybackForeground);
        Resources["UtilityHoverBackgroundBrush"] = new SolidColorBrush(highContrast
            ? SystemColors.HighlightColor
            : palette.UtilityHoverBackground);
        Resources["UtilityHoverForegroundBrush"] = new SolidColorBrush(highContrast
            ? SystemColors.HighlightTextColor
            : palette.UtilityForeground);
        Resources["NowPlayingHoverForegroundBrush"] = new SolidColorBrush(highContrast
            ? SystemColors.HighlightTextColor
            : text.Color);
        ApplyContextMenuPalette(highContrast);

        foreach (var button in new[] { PreviousButton, PlayPauseButton, NextButton })
        {
            button.Background = buttonBackground;
            button.Foreground = buttonForeground;
        }
        CycleSessionButton.Foreground = new SolidColorBrush(highContrast
            ? SystemColors.WindowTextColor
            : palette.UtilityForeground);
        DragHandle.Foreground = CycleSessionButton.Foreground;
        DragToMoveMenuItem.IsChecked = _config.DragToMove;
        DragHandle.Visibility = _config.DragToMove ? Visibility.Visible : Visibility.Collapsed;
        UpdateNowPlayingAccessibility();

        ApplyPlayerHeight();
        ApplySavedPosition();
    }

    private void ApplyContextMenuPalette(bool highContrast)
    {
        Resources["PlayerContextMenuBackgroundBrush"] = ContrastBrush(highContrast, Color.FromRgb(0x2B, 0x2B, 0x2B), SystemColors.MenuColor);
        Resources["PlayerContextMenuBorderBrush"] = ContrastBrush(highContrast, Color.FromRgb(0x54, 0x54, 0x54), SystemColors.WindowTextColor);
        Resources["PlayerContextMenuForegroundBrush"] = ContrastBrush(highContrast, Color.FromRgb(0xF1, 0xF3, 0xF4), SystemColors.MenuTextColor);
        Resources["PlayerContextMenuHighlightedBackgroundBrush"] = ContrastBrush(highContrast, Color.FromRgb(0x3D, 0x3D, 0x3D), SystemColors.HighlightColor);
        Resources["PlayerContextMenuHighlightedForegroundBrush"] = ContrastBrush(highContrast, Color.FromRgb(0xF1, 0xF3, 0xF4), SystemColors.HighlightTextColor);
        Resources["PlayerContextMenuSeparatorBrush"] = ContrastBrush(highContrast, Color.FromRgb(0x54, 0x54, 0x54), SystemColors.WindowTextColor);
    }

    private static SolidColorBrush ContrastBrush(bool highContrast, Color normal, Color contrast) =>
        new(highContrast ? contrast : normal);

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            _ = Dispatcher.InvokeAsync(ApplyConfig);
        }
    }

    private void ApplyPlayerHeight()
    {
        PlayerSurface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Height = PlayerSurface.DesiredSize.Height;
        UpdateLayout();
    }

    private void ApplyPlayerLayout(PlayerLayoutMetrics metrics)
    {
        RootBorder.Margin = new Thickness(0, 0, metrics.ResizeHitTargetOutsideWidth, 0);
        SetPlayerWidth(_config.Width);
        RootBorder.Padding = new Thickness(metrics.RootPadding);
        RootBorder.CornerRadius = new CornerRadius(metrics.RootCornerRadius);
        Resources["PlayerControlCornerRadius"] = new CornerRadius(metrics.ControlCornerRadius);
        Resources["PlayerDragIconWidth"] = metrics.DragIconWidth;
        Resources["PlayerDragIconHeight"] = metrics.DragIconHeight;

        PreviousButton.Width = metrics.PreviousNextButtonWidth;
        PreviousButton.Height = metrics.RowHeight;
        PreviousButton.Margin = new Thickness(0, 0, metrics.ControlGap, 0);
        PreviousIcon.Width = metrics.PreviousNextIconSize;
        PreviousIcon.Height = metrics.PreviousNextIconSize;

        PlayPauseButton.Width = metrics.PlayPauseButtonWidth;
        PlayPauseButton.Height = metrics.RowHeight;
        PlayPauseButton.Margin = new Thickness(0, 0, metrics.ControlGap, 0);
        PlayPauseIconContainer.Width = metrics.PlayPauseIconSize;
        PlayPauseIconContainer.Height = metrics.PlayPauseIconSize;

        NextButton.Width = metrics.PreviousNextButtonWidth;
        NextButton.Height = metrics.RowHeight;
        NextButton.Margin = new Thickness(0, 0, metrics.ControlGap, 0);
        NextIcon.Width = metrics.PreviousNextIconSize;
        NextIcon.Height = metrics.PreviousNextIconSize;

        NowPlayingHitTarget.MinHeight = metrics.RowHeight;
        NowPlayingContent.Margin = new Thickness(metrics.ContentHorizontalInset, 0, metrics.ContentHorizontalInset, 0);
        VolumeIndicator.MinWidth = metrics.VolumeIndicatorWidth;
        VolumeIndicator.Height = metrics.RowHeight;
        VolumeSpeakerIcon.Width = metrics.VolumeIconSize;
        VolumeSpeakerIcon.Height = metrics.VolumeIconSize;
        AppIconImage.Width = metrics.AppIconSize;
        AppIconImage.Height = metrics.AppIconSize;
        AppIconImage.Margin = new Thickness(metrics.AppIconGap, 0, 0, 0);

        CycleSessionButton.Width = metrics.UtilityButtonWidth;
        CycleSessionButton.Height = metrics.RowHeight;
        CycleSessionIcon.Width = metrics.CycleIconWidth;
        CycleSessionIcon.Height = metrics.CycleIconHeight;

        DragHandle.Width = metrics.UtilityButtonWidth;
        DragHandle.Height = metrics.RowHeight;
        ResizeCue.Width = metrics.ResizeCueSize;
        ResizeCue.Height = metrics.ResizeCueSize;
        ResizeCue.Margin = new Thickness(0, 0, metrics.ResizeHitTargetOutsideWidth - metrics.RootPadding, 0);
        ResizeHandle.Width = metrics.ResizeHitTargetWidth;
    }

    private void SetPlayerWidth(double width)
    {
        RootBorder.Width = width;
        Width = width + RootBorder.Margin.Right;
    }

    private void OnDisplayChanged()
    {
        if (_resumeMonitorId is null)
        {
            QueueApplyPosition();
        }
        else if (_resumeMonitorTimer.IsEnabled)
        {
            TryRestoreResumeMonitor();
        }

        _ = Dispatcher.InvokeAsync(ApplyContextMenuDpi, DispatcherPriority.Loaded);
        _ = Dispatcher.InvokeAsync(ApplyNowPlayingLineHeight, DispatcherPriority.Loaded);
        _ = RefreshMonitorOptionsAsync();
    }

    private void OnSuspending()
    {
        _resumeMonitorTimer.Stop();
        _resumeMonitorId = _config.MonitorId;
    }

    private void OnResumed()
    {
        _resumeMonitorId ??= _config.MonitorId;
        if (_resumeMonitorId is null)
        {
            QueueApplyPosition();
            return;
        }

        _resumeMonitorChecksRemaining = ResumeMonitorCheckCount;
        if (!TryRestoreResumeMonitor())
        {
            _resumeMonitorTimer.Start();
        }
    }

    private void OnResumeMonitorTimerTick()
    {
        if (_resumeMonitorId is null)
        {
            return;
        }

        if (TryRestoreResumeMonitor() || --_resumeMonitorChecksRemaining > 0)
        {
            return;
        }

        FinishResumeMonitorWait();
    }

    private bool TryRestoreResumeMonitor()
    {
        if (_displayPlacement?.IsMonitorAvailable(_resumeMonitorId) != true)
        {
            return false;
        }

        FinishResumeMonitorWait();
        return true;
    }

    private void FinishResumeMonitorWait()
    {
        CancelResumeMonitorWait();
        QueueApplyPosition();
    }

    private void CancelResumeMonitorWait()
    {
        _resumeMonitorTimer.Stop();
        _resumeMonitorId = null;
    }

    private void QueueApplyPosition()
    {
        if (_displayPlacement is null || _dragging || _positionQueued || _resumeMonitorId is not null)
        {
            return;
        }

        _positionQueued = true;
        _ = Dispatcher.InvokeAsync(() =>
        {
            _positionQueued = false;
            if (!_dragging && _resumeMonitorId is null)
            {
                ApplySavedPosition();
            }
        }, DispatcherPriority.Loaded);
    }

    private void ApplySavedPosition(bool persist = false)
    {
        if (_displayPlacement is null)
        {
            return;
        }

        try
        {
            UpdateLayout();
            var result = _displayPlacement.Place(_config);
            if (result.IsFallback)
            {
                SetPlayerWidth(result.Width);
                return;
            }

            ApplyPlacementResult(result, persist);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not place player", ex);
        }
    }

    private void ApplyPlacementResult(DisplayPlacementResult result, bool persist = false)
    {
        var previousMonitorId = _config.MonitorId;
        var previousWidth = _config.Width;
        var previousX = _config.X;
        var previousY = _config.Y;
        _config.MonitorId = result.Monitor.Id;
        _config.Width = result.Width;
        _config.X = result.X;
        _config.Y = result.Y;
        _config.Normalize();

        var changed = !string.Equals(previousMonitorId, _config.MonitorId, StringComparison.OrdinalIgnoreCase)
            || Math.Abs(previousWidth - _config.Width) > 0.01
            || Math.Abs(previousX - _config.X) > 0.01
            || Math.Abs(previousY - _config.Y) > 0.01;
        SetPlayerWidth(_config.Width);
        UpdateMonitorOptions();
        if (changed || persist)
        {
            _config.Save();
        }
    }

    private void ApplyContextMenuDpi()
    {
        foreach (var separator in PlayerContextMenu.Items.OfType<Separator>())
            separator.Height = 1 / VisualTreeHelper.GetDpi(this).DpiScaleY;
    }

    private void ApplyNowPlayingLineHeight()
    {
        var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        NowPlayingText.LineHeight = PlayerLayoutMetrics.CalculateLineHeight(
            NowPlayingHitTarget.MinHeight,
            NowPlayingText.FontFamily.LineSpacing,
            NowPlayingText.FontSize,
            dpiScale);
    }

    internal void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            if (WindowPlacement.IsNativeVisible(_settingsWindow))
            {
                WindowPlacement.CenterOnCursorMonitor(_settingsWindow);
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow.Close();
        }

        var settingsWindow = new SettingsWindow(_config, ApplySettingsConfig)
        {
            Owner = this
        };

        _settingsWindow = settingsWindow;
        UpdateMonitorOptions();
        settingsWindow.Closed += (_, _) => _settingsWindow = null;
        settingsWindow.Show();
        settingsWindow.SetUpdateStatus(_updateStatus);
    }

    private async Task RefreshMonitorOptionsAsync()
    {
        var displayPlacement = _displayPlacement;
        if (displayPlacement is null)
        {
            return;
        }

        var version = ++_monitorOptionsRefreshVersion;
        try
        {
            var options = await displayPlacement.GetMonitorOptionsAsync();
            if (version != _monitorOptionsRefreshVersion)
            {
                return;
            }

            _activeMonitorOptions = options;
            foreach (var option in options)
            {
                _knownMonitorOptions[option.Id] = option;
            }

            UpdateMonitorOptions();
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not refresh monitors", ex);
        }
    }

    private void UpdateMonitorOptions()
    {
        _knownMonitorOptions.TryGetValue(_config.MonitorId ?? "", out var knownPreferred);
        var options = DisplayMonitorOptions.IncludePreferred(
            _activeMonitorOptions,
            _config.MonitorId,
            knownPreferred);
        _settingsWindow?.SetMonitorOptions(options, _config.MonitorId);
    }

    private void SetUpdateStatus(string? status)
    {
        _updateStatus = status;
        _settingsWindow?.SetUpdateStatus(status);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void DragToMove_Click(object sender, RoutedEventArgs e)
    {
        _config.DragToMove = DragToMoveMenuItem.IsChecked;
        ApplyConfig();
        _config.Save();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private bool ApplySettingsConfig()
    {
        _renderedSnapshot = null;
        ResetVolumeIndicatorValue();
        ApplyConfig();
        var saved = _config.Save();
        _ = Dispatcher.InvokeAsync(RefreshMediaAsync);
        return saved;
    }

    private async Task RefreshMediaAsync()
    {
        if (_refreshing)
        {
            _refreshQueued = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshQueued = false;
                try
                {
                    var snapshot = await _media.GetSnapshotAsync(_config);
                    if (snapshot.ConfigChanged)
                    {
                        _config.Save();
                        _settingsWindow?.RefreshMediaApps();
                    }

                    if (_renderedSnapshot == snapshot)
                    {
                        ReportMediaRefreshSuccess();
                        continue;
                    }

                    var showAppIcon = _config.ShowIcon
                        && snapshot.HasSession
                        && (!_config.ShowIconOnlyWithMultipleSources || snapshot.EnabledSessionCount >= 2);
                    var appIcon = showAppIcon
                        ? MediaAppIconResolver.GetIcon(snapshot.SourceAppUserModelId)
                        : null;
                    var retryIcon = showAppIcon && appIcon is null;
                    if (!string.Equals(
                            _currentSourceAppUserModelId,
                            snapshot.SourceAppUserModelId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        ResetVolumeIndicatorValue();
                    }

                    _currentSourceAppUserModelId = snapshot.SourceAppUserModelId;
                    _hasMediaSession = snapshot.HasSession;
                    SetLiveText(NowPlayingText, snapshot.DisplayText);
                    AutomationProperties.SetName(
                        PlayPauseButton,
                        snapshot.IsPlaying ? "Pause" : "Play");
                    UpdateNowPlayingAccessibility();
                    var mediaAppName = _config.MediaApps.FirstOrDefault(app =>
                        string.Equals(
                            app.SourceAppUserModelId,
                            snapshot.SourceAppUserModelId,
                            StringComparison.OrdinalIgnoreCase))?.DisplayName;
                    AutomationProperties.SetName(
                        AppIconImage,
                        string.IsNullOrWhiteSpace(mediaAppName) ? "Media app icon" : $"{mediaAppName} icon");
                    AppIconImage.Source = appIcon;
                    AppIconImage.Visibility = appIcon is null ? Visibility.Collapsed : Visibility.Visible;
                    PlayIcon.Visibility = snapshot.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
                    PauseIcon.Visibility = snapshot.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
                    ControlPanel.IsEnabled = snapshot.HasSession;
                    ApplyContentOpacity(snapshot.HasSession);
                    CycleSessionButton.Visibility = snapshot.EnabledSessionCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
                    _renderedSnapshot = retryIcon ? null : snapshot;
                    ReportMediaRefreshSuccess();
                }
                catch (Exception ex)
                {
                    _renderedSnapshot = null;
                    _currentSourceAppUserModelId = null;
                    _hasMediaSession = false;
                    ResetVolumeIndicatorValue();
                    SetLiveText(NowPlayingText, "Media unavailable");
                    AutomationProperties.SetName(PlayPauseButton, "Play");
                    UpdateNowPlayingAccessibility();
                    AppIconImage.Source = null;
                    AppIconImage.Visibility = Visibility.Collapsed;
                    ControlPanel.IsEnabled = false;
                    ApplyContentOpacity(false);
                    CycleSessionButton.Visibility = Visibility.Collapsed;
                    if (EnterRefreshError(ex, ref _refreshErrorKey))
                    {
                        AppLog.Error("Could not refresh media", ex);
                    }
                }
            } while (_refreshQueued);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ReportMediaRefreshSuccess()
    {
        if (!LeaveRefreshError(ref _refreshErrorKey))
        {
            return;
        }

        AppLog.Info("Media refresh recovered");
    }

    internal static bool EnterRefreshError(Exception exception, ref string? currentKey)
    {
        var nextKey = $"{exception.GetType().FullName}: {exception.Message}";
        if (string.Equals(currentKey, nextKey, StringComparison.Ordinal))
        {
            return false;
        }

        currentKey = nextKey;
        return true;
    }

    internal static bool LeaveRefreshError(ref string? currentKey)
    {
        var recovered = currentKey is not null;
        currentKey = null;
        return recovered;
    }

    private async void Previous_Click(object sender, RoutedEventArgs e) =>
        await RunMediaCommandAsync(() => _media.PreviousAsync(_config));

    private async void PlayPause_Click(object sender, RoutedEventArgs e) =>
        await RunMediaCommandAsync(() => _media.TogglePlayPauseAsync(_config));

    private async void Next_Click(object sender, RoutedEventArgs e) =>
        await RunMediaCommandAsync(() => _media.NextAsync(_config));

    private async void CycleSession_Click(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(() => _media.CycleSessionAsync(_config));
        await RefreshMediaAsync();
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelResumeMonitorWait();
        DragHandle.Focus();
        _dragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Error("Could not move player", ex);
            return;
        }
        finally
        {
            _dragging = false;
        }

        SaveClampedPosition();
    }

    private void ResizeHandle_DragStarted(object sender, DragStartedEventArgs e)
    {
        CancelResumeMonitorWait();
        ResizeHandle.Focus();
        _dragging = true;
    }

    private void ResizeHandle_DragDelta(object sender, DragDeltaEventArgs e) =>
        SetPlayerWidth(Math.Max(MinWidth, RootBorder.Width + e.HorizontalChange));

    private void ResizeHandle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _config.Width = RootBorder.Width;
        _dragging = false;
        SaveClampedPosition();
    }

    private void ResizeHandle_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Left and not Key.Right)
        {
            return;
        }

        var delta = e.Key == Key.Left ? -1 : 1;
        SetPlayerWidth(Math.Max(MinWidth, RootBorder.Width + delta));
        _config.Width = RootBorder.Width;
        SaveClampedPosition();
        e.Handled = true;
    }

    private void DragHandle_KeyDown(object sender, KeyEventArgs e)
    {
        var direction = e.Key switch
        {
            Key.Left => DisplayDirection.Left,
            Key.Right => DisplayDirection.Right,
            Key.Up => DisplayDirection.Up,
            Key.Down => DisplayDirection.Down,
            _ => (DisplayDirection?)null
        };
        if (direction is null)
        {
            return;
        }

        CancelResumeMonitorWait();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var target = _displayPlacement?.FindDirectional(_config, direction.Value);
            if (target is not null)
            {
                _config.MonitorId = target.Id;
                ApplySavedPosition(persist: true);
            }

            e.Handled = true;
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var (x, y) = direction switch
        {
            DisplayDirection.Left => (-step, 0),
            DisplayDirection.Right => (step, 0),
            DisplayDirection.Up => (0, step),
            DisplayDirection.Down => (0, -step),
            _ => default
        };
        _config.X += x;
        _config.Y += y;

        ApplySavedPosition();
        e.Handled = true;
    }

    private void SaveClampedPosition()
    {
        if (_displayPlacement is null)
        {
            return;
        }

        try
        {
            ApplyPlacementResult(_displayPlacement.Capture(_config));
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not save player position", ex);
        }
    }

    private void ApplyContentOpacity(bool hasMedia)
    {
        var opacity = hasMedia ? 1.0 : EmptyStateOpacity;
        NowPlayingText.Opacity = opacity;
        AppIconImage.Opacity = opacity;
        ControlPanel.Opacity = opacity;
        CycleSessionButton.Opacity = opacity;
    }

    private static async Task RunMediaCommandAsync(Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception ex)
        {
            AppLog.Error("Media command failed", ex);
        }
    }

    private void NowPlayingHitTarget_Click(object sender, RoutedEventArgs e) => FocusCurrentSource();

    private void NowPlayingHitTarget_MouseEnter(object sender, MouseEventArgs e)
    {
        Volatile.Write(ref _volumeScrollHovered, true);
        var showVolume = _hasMediaSession || _config.ScrollVolumeTarget == VolumeScrollTarget.WindowsMaster;
        VolumeIndicator.Visibility = showVolume ? Visibility.Visible : Visibility.Collapsed;
        if (showVolume && !_volumeValuePinned && !VolumeIndicator.IsMouseOver)
        {
            ShowVolumeSpeaker();
        }
    }

    private void NowPlayingHitTarget_MouseLeave(object sender, MouseEventArgs e)
    {
        Volatile.Write(ref _volumeScrollHovered, false);
        VolumeIndicator.Visibility = Visibility.Collapsed;
        ResetVolumeIndicatorValue();
    }

    private void VolumeIndicator_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_volumeValuePinned)
        {
            return;
        }

        var result = AudioVolumeService.GetVolume(_currentSourceAppUserModelId, _config.ScrollVolumeTarget);
        if (result.Success)
        {
            ShowVolumePercent(result.Percent);
        }
    }

    private void VolumeIndicator_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_volumeValuePinned && NowPlayingHitTarget.IsMouseOver)
        {
            ShowVolumeSpeaker();
        }
    }

    private void NowPlayingHitTarget_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        AdjustVolume(e.Delta);
        e.Handled = true;
    }

    private void AdjustVolume(int delta)
    {
        var detents = _volumeWheel.Add(delta);
        if (detents != 0)
        {
            AdjustVolumeBySteps(detents);
        }
    }

    private bool AdjustVolumeBySteps(int steps)
    {
        var result = AudioVolumeService.AdjustVolume(
            _currentSourceAppUserModelId,
            _config.ScrollVolumeTarget,
            steps * VolumeStepPercent);
        if (!result.Success)
        {
            return false;
        }

        _volumeValuePinned = true;
        ShowVolumePercent(result.Percent);
        return true;
    }

    private void NowPlayingHitTarget_KeyDown(object sender, KeyEventArgs e)
    {
        var steps = e.Key switch
        {
            Key.Up => 1,
            Key.Down => -1,
            _ => 0
        };
        if (steps == 0 || !AdjustVolumeBySteps(steps))
        {
            return;
        }

        VolumeIndicator.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void NowPlayingHitTarget_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!NowPlayingHitTarget.IsMouseOver)
        {
            VolumeIndicator.Visibility = Visibility.Collapsed;
            ResetVolumeIndicatorValue();
        }
    }

    private void ShowVolumeSpeaker()
    {
        VolumeSpeakerIcon.Visibility = Visibility.Visible;
        VolumePercentText.Visibility = Visibility.Collapsed;
    }

    private void ShowVolumePercent(int percent)
    {
        VolumeSpeakerIcon.Visibility = Visibility.Collapsed;
        VolumePercentText.Visibility = Visibility.Visible;
        SetLiveText(VolumePercentText, $"{Math.Clamp(percent, 0, 100)}%");
    }

    private void UpdateNowPlayingAccessibility()
    {
        var controlsSystemVolume = _config.ScrollVolumeTarget == VolumeScrollTarget.WindowsMaster;
        NowPlayingHitTarget.IsTabStop = _hasMediaSession || controlsSystemVolume;
        AutomationProperties.SetName(
            NowPlayingHitTarget,
            _hasMediaSession
                ? $"Focus media source: {NowPlayingText.Text}"
                : controlsSystemVolume
                    ? $"{NowPlayingText.Text}. System volume"
                    : NowPlayingText.Text);
        var volumeHelp = $"Use Up and Down Arrow keys to change {(controlsSystemVolume ? "system" : "current app")} volume.";
        AutomationProperties.SetHelpText(
            NowPlayingHitTarget,
            _hasMediaSession
                ? $"Press Enter to focus the media source. {volumeHelp}"
                : volumeHelp);
    }

    private static void SetLiveText(TextBlock textBlock, string text)
    {
        if (string.Equals(textBlock.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        textBlock.Text = text;
        var peer = UIElementAutomationPeer.FromElement(textBlock)
            ?? UIElementAutomationPeer.CreatePeerForElement(textBlock);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void ResetVolumeIndicatorValue()
    {
        _volumeValuePinned = false;
        _volumeWheel.Reset();
        ShowVolumeSpeaker();
    }

    private void FocusCurrentSource()
    {
        var sourceAppUserModelId = _currentSourceAppUserModelId;
        if (!string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            _ = Task.Run(() => MediaSourceActivator.Open(sourceAppUserModelId));
        }
    }

    private static SolidColorBrush BrushFromConfig(string value) =>
        new((Color)ColorConverter.ConvertFromString(value)!);

}
