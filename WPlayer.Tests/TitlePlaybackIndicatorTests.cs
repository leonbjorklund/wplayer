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
}
