using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WPlayer.Services;

namespace WPlayer;

public partial class SettingsWindow : Window
{
    private readonly Func<bool> _onChanged;
    private readonly AppConfig _config;
    private string? _updateStatus;
    private string? _saveStatus;
    private string? _startupRegistrationStatus;
    private bool _loading = true;
    private bool _suppressDropdownOpen;

    internal SettingsWindow(AppConfig config, Func<bool> onChanged)
    {
        _config = config;
        _onChanged = onChanged;
        InitializeComponent();
        Loaded += PlaceOnCursorMonitor;
        LoadConfig();
    }

    private void PlaceOnCursorMonitor(object sender, RoutedEventArgs e)
    {
        Loaded -= PlaceOnCursorMonitor;
        WindowPlacement.CenterOnCursorMonitor(this);
    }

    private void LoadConfig()
    {
        _loading = true;

        ScaleSlider.Value = _config.PlayerScale * 100;
        UpdateScaleText();
        BackgroundColorTextBox.Text = _config.BackgroundColor;
        BorderColorTextBox.Text = _config.BorderColor;
        TextColorTextBox.Text = _config.TextColor;
        AppIconModeComboBox.SelectedIndex = !_config.ShowIcon
            ? 1
            : _config.ShowIconOnlyWithMultipleSources ? 2 : 0;
        var volumeTargetButton = _config.ScrollVolumeTarget == VolumeScrollTarget.WindowsMaster
            ? VolumeSystemVolumeRadio
            : VolumeCurrentAppRadio;
        volumeTargetButton.IsChecked = true;
        ShowPreviousButtonMenuItem.IsChecked = _config.ShowPreviousButton;
        ShowPlayPauseButtonMenuItem.IsChecked = _config.ShowPlayPauseButton;
        ShowNextButtonMenuItem.IsChecked = _config.ShowNextButton;
        StartupCheckBox.IsChecked = _config.LaunchAtStartup;
        UpdatePlaybackButtonsSummary();
        LoadMediaApps();
        UpdateSwatches();
        UpdateColorResetButtons();
        RefreshStatus();

        _loading = false;
    }

    public void SetUpdateStatus(string? status)
    {
        _updateStatus = status;
        RefreshStatus();
    }

    public void RefreshMediaApps() => LoadMediaApps();

    internal void SetMonitorOptions(
        IReadOnlyList<DisplayMonitorOption> monitorOptions,
        string? monitorId)
    {
        _loading = true;
        MonitorComboBox.ItemsSource = monitorOptions;
        MonitorComboBox.SelectedItem = DisplayMonitorOptions.ResolveSelected(monitorId, monitorOptions);
        var visibility = monitorOptions.Count(option => option.IsAvailable) > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        MonitorLabel.Visibility = visibility;
        MonitorComboBox.Visibility = visibility;
        _loading = false;
    }

    private void LoadMediaApps()
    {
        PlaybackAppsContextMenu.Items.Clear();

        foreach (var app in _config.MediaApps)
        {
            var menuItem = new MenuItem
            {
                Header = app.DisplayName,
                IsCheckable = true,
                IsChecked = app.Enabled,
                StaysOpenOnClick = true,
                Tag = app.SourceAppUserModelId
            };
            menuItem.Click += MediaApp_Changed;
            PlaybackAppsContextMenu.Items.Add(menuItem);
        }

        PlaybackAppsButton.IsEnabled = _config.MediaApps.Count > 0;
        UpdatePlaybackAppsSummary();
    }

    private void MediaApp_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not MenuItem { Tag: string sourceAppUserModelId } menuItem)
        {
            return;
        }

        var app = _config.MediaApps.FirstOrDefault(item =>
            string.Equals(item.SourceAppUserModelId, sourceAppUserModelId, StringComparison.OrdinalIgnoreCase));
        if (app is null)
        {
            return;
        }

        app.Enabled = menuItem.IsChecked;
        UpdatePlaybackAppsSummary();
        SaveChanges();
    }

    private void DropdownButton_Checked(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        if (_suppressDropdownOpen)
        {
            _suppressDropdownOpen = false;
            button.IsChecked = false;
            return;
        }

        var menu = button.ContextMenu!;
        menu.PlacementTarget = button;
        menu.MinWidth = button.ActualWidth;
        menu.IsOpen = true;
    }

    private void DropdownButton_Unchecked(object sender, RoutedEventArgs e) =>
        ((ToggleButton)sender).ContextMenu!.IsOpen = false;

    private void DropdownContextMenu_Opened(object sender, RoutedEventArgs e) =>
        ((ToggleButton)((ContextMenu)sender).PlacementTarget!).IsChecked = true;

    private void DropdownContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)((ContextMenu)sender).PlacementTarget!;
        var pointer = Mouse.GetPosition(button);
        _suppressDropdownOpen = Mouse.LeftButton == MouseButtonState.Pressed
            && new Rect(button.RenderSize).Contains(pointer);
        button.IsChecked = false;
    }

    private void UpdatePlaybackAppsSummary()
    {
        var enabledCount = _config.MediaApps.Count(app => app.Enabled);
        PlaybackAppsSummaryText.Text = _config.MediaApps.Count switch
        {
            0 => "No apps detected",
            _ when enabledCount == 0 => "None enabled",
            _ when enabledCount == _config.MediaApps.Count => "All apps",
            _ => $"{enabledCount} of {_config.MediaApps.Count} enabled"
        };
    }

    private void PlaybackButton_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _config.ShowPreviousButton = ShowPreviousButtonMenuItem.IsChecked;
        _config.ShowPlayPauseButton = ShowPlayPauseButtonMenuItem.IsChecked;
        _config.ShowNextButton = ShowNextButtonMenuItem.IsChecked;
        UpdatePlaybackButtonsSummary();
        SaveChanges();
    }

    private void UpdatePlaybackButtonsSummary()
    {
        var shownCount = (ShowPreviousButtonMenuItem.IsChecked ? 1 : 0)
            + (ShowPlayPauseButtonMenuItem.IsChecked ? 1 : 0)
            + (ShowNextButtonMenuItem.IsChecked ? 1 : 0);
        PlaybackButtonsSummaryText.Text = shownCount switch
        {
            0 => "None",
            3 => "All buttons",
            _ => $"{shownCount} of 3 shown"
        };
    }

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        UpdateSwatches();

        if (sender is TextBox textBox && string.IsNullOrWhiteSpace(textBox.Text))
        {
            RefreshStatus();
            return;
        }

        if (!TryReadConfig())
        {
            return;
        }

        SaveChanges();
    }

    private void VolumeTarget_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _config.ScrollVolumeTarget = sender == VolumeSystemVolumeRadio
            ? VolumeScrollTarget.WindowsMaster
            : VolumeScrollTarget.CurrentApp;
        SaveChanges();
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateScaleText();
        if (_loading)
        {
            return;
        }

        var scale = ScaleSlider.Value / 100;
        _config.Width *= scale / _config.PlayerScale;
        _config.PlayerScale = scale;
        SaveChanges();
    }

    private void UpdateScaleText() => ScaleValueText.Text = $"{ScaleSlider.Value:0}%";

    private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading
            || MonitorComboBox.SelectedItem is not DisplayMonitorOption { IsAvailable: true } option
            || string.Equals(_config.MonitorId, option.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _config.MonitorId = option.Id;
        SaveChanges();
    }

    private void SettingsPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            Keyboard.ClearFocus();
        }
    }

    private void Window_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_loading || e.OldFocus is not TextBox textBox)
        {
            return;
        }

        if (!TryReadColor(textBox.Text, out _))
        {
            LoadConfig();
            return;
        }

        UpdateColorResetButtons();
    }

    private void ResetBackgroundColor_Click(object sender, RoutedEventArgs e) =>
        ResetColor(BackgroundColorTextBox, AppConfig.DefaultBackgroundColor);

    private void ResetBorderColor_Click(object sender, RoutedEventArgs e) =>
        ResetColor(BorderColorTextBox, AppConfig.DefaultBorderColor);

    private void ResetTextColor_Click(object sender, RoutedEventArgs e) =>
        ResetColor(TextColorTextBox, AppConfig.DefaultTextColor);

    private void ResetColor(TextBox textBox, string defaultValue)
    {
        textBox.Text = defaultValue;
        UpdateColorResetButtons();
    }

    private void RefreshStatus()
    {
        var status = _saveStatus ?? _startupRegistrationStatus ?? _updateStatus;
        StatusText.Text = status ?? "";
        StatusText.Visibility = string.IsNullOrWhiteSpace(status) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveChanges()
    {
        _saveStatus = _onChanged() ? null : "Not saved";
        RefreshStatus();
    }

    private bool TryReadConfig()
    {
        if (!TryReadColor(BackgroundColorTextBox.Text, out var backgroundColor)
            || !TryReadColor(BorderColorTextBox.Text, out var borderColor)
            || !TryReadColor(TextColorTextBox.Text, out var textColor))
        {
            return false;
        }

        _config.BackgroundColor = backgroundColor;
        _config.BorderColor = borderColor;
        _config.TextColor = textColor;
        _config.ShowIcon = AppIconModeComboBox.SelectedIndex != 1;
        _config.ShowIconOnlyWithMultipleSources = AppIconModeComboBox.SelectedIndex == 2;
        var launchAtStartup = StartupCheckBox.IsChecked == true;
        if (launchAtStartup != _config.LaunchAtStartup)
        {
            _config.LaunchAtStartup = launchAtStartup;
            _startupRegistrationStatus = StartupRegistration.SetEnabled(launchAtStartup)
                ? null
                : "Startup registration unavailable";
        }
        return true;
    }

    private void UpdateSwatches()
    {
        SetSwatch(BackgroundSwatch, BackgroundColorTextBox.Text);
        SetSwatch(BorderSwatch, BorderColorTextBox.Text);
        SetSwatch(TextSwatch, TextColorTextBox.Text);
    }

    private void UpdateColorResetButtons()
    {
        SetResetButtonVisibility(ResetBackgroundColorButton, BackgroundColorTextBox.Text, AppConfig.DefaultBackgroundColor);
        SetResetButtonVisibility(ResetBorderColorButton, BorderColorTextBox.Text, AppConfig.DefaultBorderColor);
        SetResetButtonVisibility(ResetTextColorButton, TextColorTextBox.Text, AppConfig.DefaultTextColor);
    }

    private static void SetResetButtonVisibility(Button button, string value, string defaultValue) =>
        button.Visibility = string.Equals(value.Trim(), defaultValue, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;

    private static void SetSwatch(Border swatch, string text)
    {
        swatch.Background = TryReadColor(text, out var color)
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!)
            : Brushes.Transparent;
    }

    private static bool TryReadColor(string text, out string color)
    {
        color = text.Trim();
        try
        {
            _ = ColorConverter.ConvertFromString(color);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
