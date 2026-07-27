using WPlayer.Services;

namespace WPlayer.Tests;

[TestClass]
public sealed class FullscreenVisibilityGuardTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [TestMethod]
    public void KeepsFramedMaximizedWindowVisibleWhenItCoversMonitor()
    {
        Assert.IsFalse(ShouldHide(
            Monitor,
            FullscreenVisibilityPolicy.ShowMaximized,
            FullscreenVisibilityPolicy.StyleCaption | FullscreenVisibilityPolicy.StyleThickFrame));
    }

    [TestMethod]
    public void KeepsFramedMaximizedWindowVisibleWhenResizeBordersOverhangMonitor()
    {
        Assert.IsFalse(ShouldHide(
            new(-8, -8, 1928, 1088),
            FullscreenVisibilityPolicy.ShowMaximized,
            FullscreenVisibilityPolicy.StyleCaption | FullscreenVisibilityPolicy.StyleThickFrame));
    }

    [TestMethod]
    public void HidesBorderlessFullscreenWindowFromRestoredState()
    {
        Assert.IsTrue(ShouldHide(Monitor, showCommand: 1, style: 0));
    }

    [TestMethod]
    public void HidesBorderlessFullscreenWindowFromMaximizedState()
    {
        Assert.IsTrue(ShouldHide(Monitor, FullscreenVisibilityPolicy.ShowMaximized, style: 0));
    }

    [TestMethod]
    public void KeepsNonCoveringBorderlessWindowVisible()
    {
        Assert.IsFalse(ShouldHide(new(0, 0, 1920, 1077), showCommand: 1, style: 0));
    }

    [TestMethod]
    public void UsesToleranceOnNegativeCoordinateMonitor()
    {
        var monitor = new PixelRect(-2560, 100, 0, 1540);
        var window = new PixelRect(-2558, 102, -2, 1538);

        Assert.IsTrue(FullscreenVisibilityPolicy.ShouldHide(new(
            window,
            monitor,
            ShowCommand: 1,
            Style: 0)));
    }

    private static bool ShouldHide(PixelRect window, uint showCommand, uint style) =>
        FullscreenVisibilityPolicy.ShouldHide(new(window, Monitor, showCommand, style));
}
