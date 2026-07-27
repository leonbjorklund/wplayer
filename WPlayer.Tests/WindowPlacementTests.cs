using Microsoft.VisualStudio.TestTools.UnitTesting;
using WPlayer.Services;

namespace WPlayer.Tests;

[TestClass]
public sealed class WindowPlacementTests
{
    [TestMethod]
    public void CentersWindowInPrimaryWorkArea()
    {
        var centered = WindowPlacementMath.Center(
            new PixelRect(0, 0, 340, 600),
            new PixelRect(0, 0, 1920, 1040));

        Assert.AreEqual(new PixelRect(790, 220, 1130, 820), centered);
    }

    [TestMethod]
    public void CentersScaledWindowOnNegativeCoordinateMonitor()
    {
        var centered = WindowPlacementMath.Center(
            new PixelRect(0, 0, 510, 900),
            new PixelRect(-2560, 40, 0, 1440));

        Assert.AreEqual(new PixelRect(-1535, 290, -1025, 1190), centered);
    }

    [TestMethod]
    public void AnchorsOversizedWindowToConstrainedWorkArea()
    {
        var centered = WindowPlacementMath.Center(
            new PixelRect(0, 0, 500, 700),
            new PixelRect(100, 50, 500, 350));

        Assert.AreEqual(new PixelRect(100, 50, 600, 750), centered);
    }
}
