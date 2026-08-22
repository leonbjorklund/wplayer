using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WPlayer.Tests;

[TestClass]
public sealed class PlayerLayoutTests
{
    [TestMethod]
    public void VolumePercentKeepsBaselineAndCompressesThreeDigits()
    {
        StaTest.Run(() =>
        {
            var player = new MainWindow(new AppConfig());
            try
            {
                var titlePlayback = (Button)player.FindName("TitlePlayPauseIndicator");
                var volume = (Canvas)player.FindName("VolumeIndicator");
                var volumeText = (TextBlock)player.FindName("VolumePercentText");

                Assert.AreEqual(22, titlePlayback.Width);
                Assert.AreEqual(new Thickness(-0.5, 0, -0.5, 0), titlePlayback.Margin);
                Assert.AreEqual(22, volume.Width);
                Assert.AreEqual(2, Canvas.GetLeft(player.FindName("VolumeSpeakerIcon") as UIElement));
                Assert.AreEqual(12, volumeText.FontSize);

                player.ShowVolumePercent(55);
                Assert.AreEqual("55%", volumeText.Text);
                Assert.AreEqual(12, volumeText.FontSize);
                Assert.AreEqual(
                    (22 - volumeText.DesiredSize.Width) / 2,
                    Canvas.GetLeft(volumeText),
                    0.001);
                Assert.AreEqual(
                    (22 - volumeText.DesiredSize.Height) / 2 - 0.5,
                    Canvas.GetTop(volumeText),
                    0.001);
                var twoDigitTop = Canvas.GetTop(volumeText);

                player.ShowVolumePercent(100);
                Assert.AreEqual("100%", volumeText.Text);
                Assert.AreEqual(12, volumeText.FontSize);
                var scale = (ScaleTransform)volumeText.LayoutTransform;
                Assert.AreEqual(11d / 12d, scale.ScaleX, 0.001);
                Assert.AreEqual(1, scale.ScaleY);
                Assert.IsTrue(volumeText.DesiredSize.Width > 22);
                Assert.AreEqual(
                    (22 - volumeText.DesiredSize.Width) / 2,
                    Canvas.GetLeft(volumeText),
                    0.001);
                Assert.AreEqual(twoDigitTop, Canvas.GetTop(volumeText), 0.001);
            }
            finally
            {
                player.Close();
            }
        });
    }

    [TestMethod]
    [DataRow(1d)]
    [DataRow(2d)]
    public void VolumeClearancePreservesAccessoryPositionsAndTitlePlaybackSpacing(double playerScale)
    {
        StaTest.Run(() =>
        {
            var player = new MainWindow(new AppConfig { PlayerScale = playerScale });
            try
            {
                var content = (Grid)player.FindName("NowPlayingContent");
                var title = (TextBlock)player.FindName("NowPlayingText");
                var titlePlayback = (Button)player.FindName("TitlePlayPauseIndicator");
                var volume = (Canvas)player.FindName("VolumeIndicator");
                var volumeText = (TextBlock)player.FindName("VolumePercentText");
                var appIcon = (Image)player.FindName("AppIconImage");
                var arrangedSize = new Size(300 * playerScale, 22 * playerScale);

                appIcon.Visibility = Visibility.Visible;
                titlePlayback.Visibility = Visibility.Collapsed;
                volume.Visibility = Visibility.Visible;
                player.ShowVolumePercent(100);
                player.UpdateNowPlayingTextClearance();
                Arrange(content, arrangedSize);

                var volumeLeftWithoutPlayback = volume.TranslatePoint(new Point(), content).X;
                var appLeftWithoutPlayback = appIcon.TranslatePoint(new Point(), content).X;
                var titleRight = title.TranslatePoint(new Point(title.ActualWidth, 0), content).X;
                var volumeTextLeft = volumeLeftWithoutPlayback + Canvas.GetLeft(volumeText);
                Assert.AreEqual(new Thickness(0, 0, 4 * playerScale, 0), title.Margin);
                Assert.AreEqual(2 * playerScale, volumeTextLeft - titleRight, 0.001);

                titlePlayback.Visibility = Visibility.Visible;
                player.UpdateNowPlayingTextClearance();
                Arrange(content, arrangedSize);

                Assert.AreEqual(new Thickness(0), title.Margin);
                Assert.AreEqual(
                    volumeLeftWithoutPlayback,
                    volume.TranslatePoint(new Point(), content).X,
                    0.001);
                Assert.AreEqual(
                    appLeftWithoutPlayback,
                    appIcon.TranslatePoint(new Point(), content).X,
                    0.001);
            }
            finally
            {
                player.Close();
            }
        });
    }

    private static void Arrange(Grid content, Size size)
    {
        content.Measure(size);
        content.Arrange(new Rect(size));
        content.UpdateLayout();
    }
}
