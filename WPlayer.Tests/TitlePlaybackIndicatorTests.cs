using System.Windows.Input;

namespace WPlayer.Tests;

[TestClass]
public sealed class TitlePlaybackIndicatorTests
{
    [DataRow(false, true, true, true)]
    [DataRow(true, true, true, false)]
    [DataRow(false, false, true, false)]
    [DataRow(false, true, false, false)]
    [TestMethod]
    public void ShowsOnlyForHiddenButtonWithSessionWhileHovered(
        bool showPlayPauseButton,
        bool hasMediaSession,
        bool isTitleHovered,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            TitlePlaybackIndicatorPolicy.ShouldShow(
                showPlayPauseButton,
                hasMediaSession,
                isTitleHovered));
    }

    [DataRow(Key.Space, false, true, true)]
    [DataRow(Key.Space, true, true, false)]
    [DataRow(Key.Space, false, false, false)]
    [DataRow(Key.Enter, false, true, false)]
    [DataRow(Key.Up, false, true, false)]
    [TestMethod]
    public void ResolvesSpaceOnlyForTheHiddenPlaybackButton(
        Key key,
        bool showPlayPauseButton,
        bool hasMediaSession,
        bool expectedToggle)
    {
        Assert.AreEqual(
            expectedToggle ? PlaybackCommand.TogglePlayPause : PlaybackCommand.None,
            TitlePlaybackKeyboardGestureResolver.Resolve(
                key,
                showPlayPauseButton,
                hasMediaSession));
    }
}
