using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using WPlayer.Services;

namespace WPlayer.Tests;

[TestClass]
public sealed class MediaReconciliationTests
{
    [TestMethod]
    public void PlaybackCommandReconcilesWithoutAPlaybackEvent()
    {
        StaTest.Run(() =>
        {
            var isPlaying = true;
            var refreshCount = 0;
            var player = CreatePlayer(
                () =>
                {
                    isPlaying = false;
                    return Task.CompletedTask;
                },
                () =>
                {
                    refreshCount++;
                    return Snapshot(isPlaying);
                });
            try
            {
                player.UpdatePlaybackState(isPlaying: true);
                ((Button)player.FindName("PlayPauseButton")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));

                StaTest.Pump(TimeSpan.FromMilliseconds(750));

                Assert.AreEqual("Play", AutomationProperties.GetName((Button)player.FindName("PlayPauseButton")));
                Assert.AreEqual(1, refreshCount);
            }
            finally
            {
                player.Close();
            }
        });
    }

    [TestMethod]
    public void RapidNotificationsReconcileOnceAfterStateSettles()
    {
        StaTest.Run(() =>
        {
            var isPlaying = true;
            var refreshCount = 0;
            var player = CreatePlayer(
                () => Task.CompletedTask,
                () =>
                {
                    refreshCount++;
                    return Snapshot(isPlaying);
                });
            try
            {
                player.UpdatePlaybackState(isPlaying: true);
                player.ScheduleMediaReconciliation();

                var finalNotification = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                finalNotification.Tick += (_, _) =>
                {
                    finalNotification.Stop();
                    isPlaying = false;
                    player.ScheduleMediaReconciliation();
                };
                finalNotification.Start();

                StaTest.Pump(TimeSpan.FromMilliseconds(850));

                Assert.AreEqual("Play", AutomationProperties.GetName((Button)player.FindName("PlayPauseButton")));
                Assert.AreEqual(1, refreshCount);
            }
            finally
            {
                player.Close();
            }
        });
    }

    private static MainWindow CreatePlayer(
        Func<Task> playbackCommand,
        Func<Task<MediaSnapshot>> mediaSnapshot) =>
        new(
            new AppConfig { ShowIcon = false },
            _ => playbackCommand(),
            mediaSnapshot);

    private static Task<MediaSnapshot> Snapshot(bool isPlaying) =>
        Task.FromResult(new MediaSnapshot(true, isPlaying, "Test media", "test", EnabledSessionCount: 1));
}
