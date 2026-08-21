namespace WPlayer.Tests;

[TestClass]
public sealed class AppConfigTests
{
    [TestMethod]
    public void NormalizeRepairsInvalidValuesAndBounds()
    {
        var config = new AppConfig
        {
            Width = double.NaN,
            MonitorId = "  display-id  ",
            X = double.PositiveInfinity,
            Y = double.NegativeInfinity,
            PlayerScale = double.PositiveInfinity,
            ScrollVolumeTarget = (VolumeScrollTarget)99,
            BackgroundColor = "invalid",
            BorderColor = " ",
            TextColor = " #ffffff "
        };

        config.Normalize();

        Assert.AreEqual(AppConfig.DefaultWidth, config.Width);
        Assert.AreEqual("display-id", config.MonitorId);
        Assert.AreEqual(AppConfig.DefaultX, config.X);
        Assert.AreEqual(AppConfig.DefaultY, config.Y);
        Assert.AreEqual(AppConfig.DefaultPlayerScale, config.PlayerScale);
        Assert.AreEqual(VolumeScrollTarget.CurrentApp, config.ScrollVolumeTarget);
        Assert.AreEqual(AppConfig.DefaultBackgroundColor, config.BackgroundColor);
        Assert.AreEqual(AppConfig.DefaultBorderColor, config.BorderColor);
        Assert.AreEqual("#ffffff", config.TextColor);
    }

    [TestMethod]
    public void NormalizeCleansAndDeduplicatesMediaApps()
    {
        var config = new AppConfig
        {
            MediaApps =
            [
                new(" app.one ", " First ", true),
                new("APP.ONE", "Duplicate", false),
                new("app.two", " ", false),
                new(" ", "Blank", true)
            ]
        };

        config.Normalize();

        Assert.HasCount(2, config.MediaApps);
        Assert.AreEqual("app.one", config.MediaApps[0].SourceAppUserModelId);
        Assert.AreEqual("First", config.MediaApps[0].DisplayName);
        Assert.IsTrue(config.MediaApps[0].Enabled);
        Assert.AreEqual("app.two", config.MediaApps[1].DisplayName);
        Assert.IsFalse(config.MediaApps[1].Enabled);
    }

    [TestMethod]
    public void NormalizeClampsMinimumsAndPreservesFinitePosition()
    {
        var config = new AppConfig { Width = 100, X = -20, Y = 40, PlayerScale = 3 };

        config.Normalize();

        Assert.AreEqual(220, config.Width);
        Assert.AreEqual(-20, config.X);
        Assert.AreEqual(40, config.Y);
        Assert.AreEqual(2, config.PlayerScale);
    }

    [TestMethod]
    [DataRow(0.1, 1)]
    [DataRow(0.77, 1)]
    [DataRow(1.13, 1.15)]
    [DataRow(1.98, 2)]
    [DataRow(3, 2)]
    public void NormalizeClampsAndSnapsPlayerScale(double value, double expected)
    {
        var config = new AppConfig { PlayerScale = value };

        config.Normalize();

        Assert.AreEqual(expected, config.PlayerScale);
    }

    [TestMethod]
    public void NormalizeKeepsDefaultWidthIndependentFromScale()
    {
        var config = new AppConfig { PlayerScale = 2, Width = double.NaN };

        config.Normalize();

        Assert.AreEqual(AppConfig.DefaultWidth, config.Width);
    }

    [TestMethod]
    public void NormalizePreservesFractionalPosition()
    {
        var config = new AppConfig { X = 2.149, Y = -2.5 };

        config.Normalize();

        Assert.AreEqual(2.149, config.X);
        Assert.AreEqual(-2.5, config.Y);
    }

    [TestMethod]
    public void ScrollVolumeTargetDefaultsWhenMissing()
    {
        var config = AppConfig.Deserialize("{}");

        Assert.AreEqual(VolumeScrollTarget.CurrentApp, config.ScrollVolumeTarget);
    }

    [TestMethod]
    public void LoadPreservesSettingsFromPreviousRelease()
    {
        RunInTempDirectory(directory =>
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, """
                {
                  "mediaApps": [
                    {
                      "sourceAppUserModelId": "Spotify.exe",
                      "displayName": "Spotify",
                      "enabled": false
                    }
                  ],
                  "width": 420,
                  "monitorId": "display-id",
                  "x": -12.5,
                  "y": 32.25,
                  "playerScale": 1.25,
                  "showPreviousButton": false,
                  "showPlayPauseButton": true,
                  "showNextButton": false,
                  "showIcon": false,
                  "showIconOnlyWithMultipleSources": true,
                  "dragToMove": false,
                  "launchAtStartup": false,
                  "scrollVolumeTarget": "windowsMaster",
                  "backgroundColor": "#112233",
                  "borderColor": "#445566",
                  "textColor": "#AABBCC"
                }
                """);

            var config = AppConfig.Load(path);

            Assert.HasCount(1, config.MediaApps);
            Assert.AreEqual("Spotify.exe", config.MediaApps[0].SourceAppUserModelId);
            Assert.AreEqual("Spotify", config.MediaApps[0].DisplayName);
            Assert.IsFalse(config.MediaApps[0].Enabled);
            Assert.AreEqual(420, config.Width);
            Assert.AreEqual("display-id", config.MonitorId);
            Assert.AreEqual(-12.5, config.X);
            Assert.AreEqual(32.25, config.Y);
            Assert.AreEqual(1.25, config.PlayerScale);
            Assert.IsFalse(config.ShowPreviousButton);
            Assert.IsTrue(config.ShowPlayPauseButton);
            Assert.IsFalse(config.ShowNextButton);
            Assert.IsFalse(config.ShowIcon);
            Assert.IsTrue(config.ShowIconOnlyWithMultipleSources);
            Assert.IsFalse(config.DragToMove);
            Assert.IsFalse(config.LaunchAtStartup);
            Assert.AreEqual(VolumeScrollTarget.WindowsMaster, config.ScrollVolumeTarget);
            Assert.AreEqual("#112233", config.BackgroundColor);
            Assert.AreEqual("#445566", config.BorderColor);
            Assert.AreEqual("#AABBCC", config.TextColor);
        });
    }

    [TestMethod]
    public void ColorsUseSimplifiedDefaults()
    {
        var config = new AppConfig();

        Assert.AreEqual("#1C1C1C", config.BackgroundColor);
        Assert.AreEqual("Transparent", config.BorderColor);
        Assert.AreEqual("#F1F3F4", config.TextColor);
    }

    [TestMethod]
    public void PlayerScaleDefaultsWhenMissing()
    {
        var config = AppConfig.Deserialize("{}");

        Assert.AreEqual(AppConfig.DefaultPlayerScale, config.PlayerScale);
    }

    [TestMethod]
    public void PlaybackButtonVisibilityDefaultsWhenMissing()
    {
        var config = AppConfig.Deserialize("""{"showControls":false}""");

        Assert.IsTrue(config.ShowPreviousButton);
        Assert.IsTrue(config.ShowPlayPauseButton);
        Assert.IsTrue(config.ShowNextButton);
    }

    [TestMethod]
    public void PlaybackButtonVisibilityRoundTrips()
    {
        var json = AppConfig.Serialize(new AppConfig
        {
            ShowPreviousButton = false,
            ShowPlayPauseButton = true,
            ShowNextButton = false
        });
        var config = AppConfig.Deserialize(json);

        StringAssert.Contains(json, "\"showPreviousButton\": false");
        StringAssert.Contains(json, "\"showPlayPauseButton\": true");
        StringAssert.Contains(json, "\"showNextButton\": false");
        Assert.IsFalse(json.Contains("showControls", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(config.ShowPreviousButton);
        Assert.IsTrue(config.ShowPlayPauseButton);
        Assert.IsFalse(config.ShowNextButton);
    }

    [TestMethod]
    public void PlayerScaleRoundTrips()
    {
        var json = AppConfig.Serialize(new AppConfig { PlayerScale = 1.25 });

        StringAssert.Contains(json, "\"playerScale\": 1.25");
        Assert.AreEqual(1.25, AppConfig.Deserialize(json).PlayerScale);
        Assert.IsFalse(json.Contains("playerPadding", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("fontSize", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ScrollVolumeTargetRoundTripsAsCamelCaseString()
    {
        var json = AppConfig.Serialize(new AppConfig
        {
            ScrollVolumeTarget = VolumeScrollTarget.WindowsMaster
        });

        StringAssert.Contains(json, "\"scrollVolumeTarget\": \"windowsMaster\"");
        Assert.AreEqual(VolumeScrollTarget.WindowsMaster, AppConfig.Deserialize(json).ScrollVolumeTarget);
    }

    [TestMethod]
    public void SaveWritesAtomically()
    {
        RunInTempDirectory(directory =>
        {
            var path = Path.Combine(directory, "config.json");

            Assert.IsTrue(new AppConfig { Width = 420 }.Save(path));
            Assert.AreEqual(420, AppConfig.Load(path).Width);
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
        });
    }

    [TestMethod]
    public void LoadBacksUpCorruptedConfigAndRecoversDefaults()
    {
        RunInTempDirectory(directory =>
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, "not json");

            var config = AppConfig.Load(path);

            Assert.AreEqual(AppConfig.DefaultWidth, config.Width);
            Assert.HasCount(1, Directory.GetFiles(directory, "config.bad-*.json"));
            Assert.AreEqual(AppConfig.DefaultWidth, AppConfig.Load(path).Width);
        });
    }

    [TestMethod]
    public void SaveReportsWriteFailure()
    {
        RunInTempDirectory(directory =>
        {
            var blockingFile = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(blockingFile, "blocked");

            Assert.IsFalse(new AppConfig().Save(Path.Combine(blockingFile, "config.json")));
        });
    }

    [TestMethod]
    public void DeleteLocalDataRemovesDirectory()
    {
        RunInTempDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "config.json"), "{}");
            Directory.CreateDirectory(Path.Combine(directory, "nested"));

            AppConfig.DeleteLocalData(directory);

            Assert.IsFalse(Directory.Exists(directory));
        });
    }

    private static void RunInTempDirectory(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"WPlayer.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            action(path);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
