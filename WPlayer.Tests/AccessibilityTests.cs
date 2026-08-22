using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WPlayer.Tests;

[TestClass]
public sealed class AccessibilityTests
{
    [TestMethod]
    public void SettingsExposeNamesHeadingsAndLiveStatus()
    {
        StaTest.Run(() =>
        {
            var config = new AppConfig
            {
                MediaApps =
                [
                    new MediaAppConfig("first", "First", true),
                    new MediaAppConfig("second", "Second", true)
                ]
            };
            var settings = new SettingsWindow(config, () => true);
            try
            {
                var appsButton = (Button)settings.FindName("PlaybackAppsButton");
                var buttonsButton = (Button)settings.FindName("PlaybackButtonsButton");
                var playbackHeading = (TextBlock)settings.FindName("PlaybackHeading");
                var appearanceHeading = (TextBlock)settings.FindName("AppearanceHeading");
                var status = (TextBlock)settings.FindName("StatusText");

                Assert.AreEqual("WPlayer Settings", settings.Title);
                Assert.AreEqual("Playback apps, All apps", AutomationProperties.GetName(appsButton));
                Assert.AreEqual("Playback buttons, All buttons", AutomationProperties.GetName(buttonsButton));
                Assert.AreEqual(AutomationHeadingLevel.Level2, AutomationProperties.GetHeadingLevel(playbackHeading));
                Assert.AreEqual(AutomationHeadingLevel.Level2, AutomationProperties.GetHeadingLevel(appearanceHeading));
                Assert.AreEqual(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(status));

                settings.SetUpdateStatus("Update available");

                Assert.AreEqual("Update available", status.Text);
                Assert.AreEqual(Visibility.Visible, status.Visibility);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void PlayerExposesInitialActionsAndLiveRegions()
    {
        StaTest.Run(() =>
        {
            var player = new MainWindow(new AppConfig());
            try
            {
                var playPause = (Button)player.FindName("PlayPauseButton");
                var titlePlayPause = (Button)player.FindName("TitlePlayPauseIndicator");
                var nowPlayingButton = (Button)player.FindName("NowPlayingHitTarget");
                var nowPlayingText = (TextBlock)player.FindName("NowPlayingText");
                var volumeText = (TextBlock)player.FindName("VolumePercentText");

                Assert.AreEqual("Play", AutomationProperties.GetName(playPause));
                Assert.AreEqual("Play", AutomationProperties.GetName(titlePlayPause));
                Assert.IsFalse(titlePlayPause.IsTabStop);
                Assert.AreEqual("Nothing playing", AutomationProperties.GetName(nowPlayingButton));
                Assert.IsFalse(nowPlayingButton.IsTabStop);
                Assert.AreEqual(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(nowPlayingText));
                Assert.AreEqual(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(volumeText));
            }
            finally
            {
                player.Close();
            }
        });
    }

    [TestMethod]
    public void HoverPlaybackActionTracksStateAndStopsTheTitleClick()
    {
        StaTest.Run(() =>
        {
            var playbackCommands = new List<PlaybackCommand>();
            var player = new MainWindow(
                new AppConfig { ShowPlayPauseButton = false },
                command =>
                {
                    playbackCommands.Add(command);
                    return Task.CompletedTask;
                });
            try
            {
                var titlePlayPause = (Button)player.FindName("TitlePlayPauseIndicator");
                var nowPlayingButton = (Button)player.FindName("NowPlayingHitTarget");
                var titleClicks = 0;
                nowPlayingButton.Click += (_, _) => titleClicks++;

                player.UpdatePlaybackState(isPlaying: true);
                Assert.AreEqual("Pause", AutomationProperties.GetName(titlePlayPause));

                var click = new RoutedEventArgs(Button.ClickEvent);
                titlePlayPause.RaiseEvent(click);

                Assert.IsTrue(click.Handled);
                Assert.AreEqual(0, titleClicks);
                CollectionAssert.AreEqual(
                    new[] { PlaybackCommand.TogglePlayPause },
                    playbackCommands);
            }
            finally
            {
                player.Close();
            }
        });
    }

    [TestMethod]
    public void SystemVolumeRemainsKeyboardReachableWithoutMedia()
    {
        StaTest.Run(() =>
        {
            var player = new MainWindow(new AppConfig
            {
                ScrollVolumeTarget = VolumeScrollTarget.WindowsMaster
            });
            try
            {
                var nowPlayingButton = (Button)player.FindName("NowPlayingHitTarget");

                Assert.IsTrue(nowPlayingButton.IsTabStop);
                Assert.AreEqual("Nothing playing. System volume", AutomationProperties.GetName(nowPlayingButton));
                StringAssert.Contains(AutomationProperties.GetHelpText(nowPlayingButton), "system volume");
                Assert.IsFalse(AutomationProperties.GetHelpText(nowPlayingButton).Contains("Enter", StringComparison.Ordinal));
            }
            finally
            {
                player.Close();
            }
        });
    }

    [TestMethod]
    public void SettingsDropdownsOpenWithEnterOrSpace()
    {
        StaTest.Run(() =>
        {
            var settings = new SettingsWindow(new AppConfig(), () => true);
            try
            {
                settings.Show();
                var appIcons = (ComboBox)settings.FindName("AppIconModeComboBox");
                var monitor = (ComboBox)settings.FindName("MonitorComboBox");
                var inputSource = PresentationSource.FromVisual(settings)!;

                var enter = new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, Key.Enter)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };
                appIcons.RaiseEvent(enter);
                Assert.IsTrue(appIcons.IsDropDownOpen);
                appIcons.IsDropDownOpen = false;

                monitor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, Key.Space)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                Assert.IsTrue(monitor.IsDropDownOpen);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void VolumeChoicesUseKeyboardOnlyTwoToneFocusVisual()
    {
        StaTest.Run(() =>
        {
            var settings = new SettingsWindow(new AppConfig(), () => true);
            try
            {
                var currentApp = (RadioButton)settings.FindName("VolumeCurrentAppRadio");
                var system = (RadioButton)settings.FindName("VolumeSystemVolumeRadio");
                var selected = (SolidColorBrush)settings.FindResource("VolumeSelectedBackgroundBrush");
                var hover = (SolidColorBrush)settings.FindResource("VolumeHoverBackgroundBrush");

                Assert.AreSame(currentApp.FocusVisualStyle, system.FocusVisualStyle);
                Assert.AreEqual(Color.FromRgb(0x38, 0x38, 0x38), selected.Color);
                Assert.AreEqual(Color.FromRgb(0x2D, 0x2D, 0x2D), hover.Color);
            }
            finally
            {
                settings.Close();
            }
        });
    }
}
