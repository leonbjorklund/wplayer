using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WPlayer.Services;

namespace WPlayer.Tests;

[TestClass]
public sealed class SettingsWindowTests
{
    [TestMethod]
    public void UsesManualStartupPlacement()
    {
        StaTest.Run(() =>
        {
            var settings = new SettingsWindow(new AppConfig(), () => true);
            try
            {
                Assert.AreEqual(WindowStartupLocation.Manual, settings.WindowStartupLocation);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void MonitorSelectorIsVisibleOnlyWithMultipleActiveMonitors()
    {
        StaTest.Run(() =>
        {
            var settings = new SettingsWindow(new AppConfig { MonitorId = "primary" }, () => true);
            try
            {
                var label = (TextBlock)settings.FindName("MonitorLabel");
                var comboBox = (ComboBox)settings.FindName("MonitorComboBox");
                var primary = new DisplayMonitorOption("primary", 1, "Primary", true);
                var secondary = new DisplayMonitorOption("secondary", 2, "Secondary", false);

                settings.SetMonitorOptions([primary], "primary");
                Assert.AreEqual(Visibility.Collapsed, label.Visibility);
                Assert.AreEqual(Visibility.Collapsed, comboBox.Visibility);

                settings.SetMonitorOptions([primary, secondary], "primary");
                Assert.AreEqual(Visibility.Visible, label.Visibility);
                Assert.AreEqual(Visibility.Visible, comboBox.Visibility);
                Assert.AreSame(primary, comboBox.SelectedItem);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void MonitorSelectorSavesUserSelectionButNotProgrammaticSync()
    {
        StaTest.Run(() =>
        {
            var saveCount = 0;
            var config = new AppConfig { MonitorId = "primary" };
            var settings = new SettingsWindow(config, () =>
            {
                saveCount++;
                return true;
            });
            try
            {
                var comboBox = (ComboBox)settings.FindName("MonitorComboBox");
                var primary = new DisplayMonitorOption("primary", 1, "Primary", true);
                var secondary = new DisplayMonitorOption("secondary", 2, "Secondary", false);
                DisplayMonitorOption[] options = [primary, secondary];
                settings.SetMonitorOptions(options, config.MonitorId);

                comboBox.SelectedItem = secondary;
                Assert.AreEqual("secondary", config.MonitorId);
                Assert.AreEqual(1, saveCount);

                config.MonitorId = "primary";
                settings.SetMonitorOptions(options, config.MonitorId);
                Assert.AreSame(primary, comboBox.SelectedItem);
                Assert.AreEqual(1, saveCount);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void MonitorSelectorRetainsUnavailablePreferredMonitor()
    {
        StaTest.Run(() =>
        {
            var config = new AppConfig { MonitorId = "preferred" };
            var settings = new SettingsWindow(config, () => true);
            try
            {
                var active = new[]
                {
                    new DisplayMonitorOption("primary", 1, "Primary", true),
                    new DisplayMonitorOption("secondary", 2, "Secondary", false)
                };
                var knownPreferred = new DisplayMonitorOption("preferred", 3, "Preferred", false);
                var options = DisplayMonitorOptions.IncludePreferred(active, config.MonitorId, knownPreferred);

                settings.SetMonitorOptions(options, config.MonitorId);

                var selected = (DisplayMonitorOption)((ComboBox)settings.FindName("MonitorComboBox")).SelectedItem;
                Assert.AreEqual("(3) Preferred — unavailable", selected.Label);
                Assert.IsFalse(selected.IsAvailable);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void ScaleSliderLoadsAndSavesInFivePercentSteps()
    {
        StaTest.Run(() =>
        {
            var saveCount = 0;
            var config = new AppConfig { PlayerScale = 1.25, Width = 360 };
            var settings = new SettingsWindow(config, () =>
            {
                saveCount++;
                return true;
            });
            try
            {
                var slider = (Slider)settings.FindName("ScaleSlider");
                var value = (TextBlock)settings.FindName("ScaleValueText");

                Assert.AreEqual(100, slider.Minimum);
                Assert.AreEqual(200, slider.Maximum);
                Assert.AreEqual(5, slider.TickFrequency);
                Assert.IsTrue(slider.IsSnapToTickEnabled);
                Assert.AreEqual(125, slider.Value);
                Assert.AreEqual("125%", value.Text);

                slider.Value = 150;

                Assert.AreEqual(1.5, config.PlayerScale);
                Assert.AreEqual(432, config.Width);
                Assert.AreEqual("150%", value.Text);
                Assert.AreEqual(1, saveCount);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void AppIconModesLoadAndSaveInDisplayOrder()
    {
        StaTest.Run(() =>
        {
            var config = new AppConfig();
            var settings = new SettingsWindow(config, () => true);
            try
            {
                var comboBox = (ComboBox)settings.FindName("AppIconModeComboBox");

                Assert.AreEqual("Show", ((ComboBoxItem)comboBox.Items[0]).Content);
                Assert.AreEqual("Hide", ((ComboBoxItem)comboBox.Items[1]).Content);
                Assert.AreEqual("Multiple sources", ((ComboBoxItem)comboBox.Items[2]).Content);
                Assert.AreEqual(0, comboBox.SelectedIndex);

                comboBox.SelectedIndex = 1;
                Assert.IsFalse(config.ShowIcon);
                Assert.IsFalse(config.ShowIconOnlyWithMultipleSources);

                comboBox.SelectedIndex = 2;
                Assert.IsTrue(config.ShowIcon);
                Assert.IsTrue(config.ShowIconOnlyWithMultipleSources);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void VolumeTargetButtonsLoadAndSave()
    {
        StaTest.Run(() =>
        {
            var saveCount = 0;
            var config = new AppConfig { ScrollVolumeTarget = VolumeScrollTarget.WindowsMaster };
            var settings = new SettingsWindow(config, () =>
            {
                saveCount++;
                return true;
            });
            try
            {
                var currentButton = (RadioButton)settings.FindName("VolumeCurrentAppRadio");
                var systemButton = (RadioButton)settings.FindName("VolumeSystemVolumeRadio");

                Assert.IsTrue(systemButton.IsChecked);
                Assert.IsFalse(currentButton.IsChecked);

                currentButton.IsChecked = true;

                Assert.AreEqual(VolumeScrollTarget.CurrentApp, config.ScrollVolumeTarget);
                Assert.IsTrue(currentButton.IsChecked);
                Assert.IsFalse(systemButton.IsChecked);
                Assert.AreEqual(1, saveCount);

                systemButton.IsChecked = true;

                Assert.AreEqual(VolumeScrollTarget.WindowsMaster, config.ScrollVolumeTarget);
                Assert.IsTrue(systemButton.IsChecked);
                Assert.IsFalse(currentButton.IsChecked);
                Assert.AreEqual(2, saveCount);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void ColorResetButtonAppearsAfterBlur()
    {
        StaTest.Run(() =>
        {
            var config = new AppConfig();
            var settings = new SettingsWindow(config, () => true);
            try
            {
                var textBox = (TextBox)settings.FindName("BackgroundColorTextBox");
                var reset = (Button)settings.FindName("ResetBackgroundColorButton");

                textBox.Text = "#111111";
                Assert.AreEqual(Visibility.Collapsed, reset.Visibility);

                settings.RaiseEvent(new KeyboardFocusChangedEventArgs(
                    Keyboard.PrimaryDevice,
                    0,
                    textBox,
                    settings)
                {
                    RoutedEvent = Keyboard.LostKeyboardFocusEvent
                });

                Assert.AreEqual(Visibility.Visible, reset.Visibility);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void ColorResetButtonsResetOnlyTheirOwnFields()
    {
        StaTest.Run(() =>
        {
            var config = new AppConfig
            {
                BackgroundColor = "#111111",
                BorderColor = "#222222",
                TextColor = "#333333"
            };
            var saveCount = 0;
            var settings = new SettingsWindow(config, () =>
            {
                saveCount++;
                return true;
            });
            try
            {
                var backgroundReset = (Button)settings.FindName("ResetBackgroundColorButton");
                var borderReset = (Button)settings.FindName("ResetBorderColorButton");
                var textReset = (Button)settings.FindName("ResetTextColorButton");

                Assert.AreEqual(Visibility.Visible, backgroundReset.Visibility);
                Assert.AreEqual(Visibility.Visible, borderReset.Visibility);
                Assert.AreEqual(Visibility.Visible, textReset.Visibility);

                backgroundReset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.AreEqual(AppConfig.DefaultBackgroundColor, config.BackgroundColor);
                Assert.AreEqual("#222222", config.BorderColor);
                Assert.AreEqual("#333333", config.TextColor);
                Assert.AreEqual(Visibility.Collapsed, backgroundReset.Visibility);

                borderReset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                textReset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.AreEqual(AppConfig.DefaultBorderColor, config.BorderColor);
                Assert.AreEqual(AppConfig.DefaultTextColor, config.TextColor);
                Assert.AreEqual(3, saveCount);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void InvalidColorRestoresLastValidValueOnBlurWithoutStatus()
    {
        StaTest.Run(() =>
        {
            var config = new AppConfig { BackgroundColor = "#111111" };
            var settings = new SettingsWindow(config, () => true);
            try
            {
                var textBox = (TextBox)settings.FindName("BackgroundColorTextBox");
                var status = (TextBlock)settings.FindName("StatusText");
                textBox.Text = "invalid";

                settings.RaiseEvent(new KeyboardFocusChangedEventArgs(
                    Keyboard.PrimaryDevice,
                    0,
                    textBox,
                    settings)
                {
                    RoutedEvent = Keyboard.LostKeyboardFocusEvent
                });

                Assert.AreEqual("#111111", textBox.Text);
                Assert.AreEqual("#111111", config.BackgroundColor);
                Assert.AreEqual(Visibility.Collapsed, status.Visibility);
            }
            finally
            {
                settings.Close();
            }
        });
    }

    [TestMethod]
    public void PlaybackButtonMenuItemsLoadAndSaveIndependently()
    {
        StaTest.Run(() =>
        {
            var saveCount = 0;
            var config = new AppConfig
            {
                ShowPreviousButton = false,
                ShowPlayPauseButton = true,
                ShowNextButton = false
            };
            var settings = new SettingsWindow(config, () =>
            {
                saveCount++;
                return true;
            });
            try
            {
                var previous = (MenuItem)settings.FindName("ShowPreviousButtonMenuItem");
                var playPause = (MenuItem)settings.FindName("ShowPlayPauseButtonMenuItem");
                var next = (MenuItem)settings.FindName("ShowNextButtonMenuItem");

                Assert.AreSame(playPause, settings.PlaybackButtonsButton.ContextMenu.Items[0]);
                Assert.IsFalse(previous.IsChecked);
                Assert.IsTrue(playPause.IsChecked);
                Assert.IsFalse(next.IsChecked);

                previous.IsChecked = true;
                previous.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                playPause.IsChecked = false;
                playPause.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                next.IsChecked = true;
                next.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.IsTrue(config.ShowPreviousButton);
                Assert.IsFalse(config.ShowPlayPauseButton);
                Assert.IsTrue(config.ShowNextButton);
                Assert.AreEqual(3, saveCount);
            }
            finally
            {
                settings.Close();
            }
        });
    }
}
