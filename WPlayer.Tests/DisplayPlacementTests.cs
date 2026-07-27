using WPlayer.Services;

namespace WPlayer.Tests;

[TestClass]
public sealed class DisplayPlacementTests
{
    [TestMethod]
    public void AllowsPlacementOverTaskbarAndClampsToMonitorBounds()
    {
        var monitor = Monitor("primary", new(0, 0, 1920, 1080), true);

        var placement = DisplayPlacementMath.Place(monitor, 5000, -50, 360, Content(monitor, 360, 40));

        Assert.AreEqual(1560, placement.WindowRect.Left);
        Assert.AreEqual(1040, placement.WindowRect.Top);
        Assert.AreEqual(1080, placement.WindowRect.Bottom);
        Assert.AreEqual(0, placement.Y);
    }

    [TestMethod]
    public void ClampsWindowInsideBounds()
    {
        var bounds = new PixelRect(0, 0, 1920, 1040);

        var clamped = DisplayPlacementMath.Clamp(new PixelRect(1500, 800, 1900, 1080), bounds);

        Assert.AreEqual(new PixelRect(1500, 760, 1900, 1040), clamped);
    }

    [TestMethod]
    [DataRow(100, 96u)]
    [DataRow(125, 120u)]
    [DataRow(150, 144u)]
    [DataRow(200, 192u)]
    public void LogicalOffsetsRoundTripAcrossScaling(int percent, uint dpi)
    {
        Assert.AreEqual(dpi, DisplayPlacementMath.ScalePercentToDpi(percent));
        var monitor = Monitor("display", new(-2400, 200, 0, 1600), dpi: dpi);
        var placed = DisplayPlacementMath.Place(monitor, 17, 23, 360, Content(monitor, 360, 40));

        var captured = DisplayPlacementMath.Capture(monitor, placed.WindowRect, 360);

        Assert.AreEqual(17, captured.X, 0.5);
        Assert.AreEqual(23, captured.Y, 0.5);
        Assert.AreEqual(360, captured.Width, 0.5);
    }

    [TestMethod]
    public void VerticalAndHeightChangesPreserveHorizontalPlacement()
    {
        var monitor = Monitor("display", new(0, 0, 3840, 2160), dpi: 144);
        var content = new PixelRect(11, 2106, 551, 2152);

        var first = DisplayPlacementMath.Place(monitor, 6, 1, 360, content);
        var moved = DisplayPlacementMath.Place(monitor, 6, 2, 360, content);
        var padded = DisplayPlacementMath.Place(monitor, 6, 2, 360, content with { Top = 2100 });

        Assert.AreEqual(first.WindowRect.Left, moved.WindowRect.Left);
        Assert.AreEqual(moved.WindowRect.Left, padded.WindowRect.Left);
        Assert.AreEqual(moved.WindowRect.Bottom, padded.WindowRect.Bottom);
        Assert.AreEqual(first.WindowRect.Bottom - 1, moved.WindowRect.Bottom);
    }

    [TestMethod]
    public void MissingMonitorFallsBackToPrimary()
    {
        var primary = Monitor("primary", new(0, 0, 1920, 1080), true);
        var secondary = Monitor("secondary", new(-1920, 0, 0, 1080));

        var selected = DisplayPlacementMath.ResolveMonitor("missing", [secondary, primary], out var missing);

        Assert.AreSame(primary, selected);
        Assert.IsTrue(missing);
    }

    [TestMethod]
    public void FindsSavedMonitorWhenAnotherMonitorIsPrimary()
    {
        var primary = Monitor("primary", new(0, 0, 1920, 1080), true);
        var saved = Monitor("saved", new(-1920, 0, 0, 1080));

        var selected = DisplayPlacementMath.ResolveMonitor("SAVED", [primary, saved], out var missing);

        Assert.AreSame(saved, DisplayPlacementMath.FindMonitor("SAVED", [primary, saved]));
        Assert.AreSame(saved, selected);
        Assert.IsFalse(missing);
    }

    [TestMethod]
    public void MissingMonitorRemainsUnavailable()
    {
        var primary = Monitor("primary", new(0, 0, 1920, 1080), true);

        Assert.IsNull(DisplayPlacementMath.FindMonitor("missing", [primary]));
    }

    [TestMethod]
    public void PreservesPlayerOffsetsWhenDisplayGeometryChanges()
    {
        var before = Monitor("display", new(0, 0, 1920, 1080));
        var after = Monitor("display", new(0, 0, 2560, 1440));

        var first = DisplayPlacementMath.Place(before, 12, 18, 360, Content(before, 360, 40));
        var second = DisplayPlacementMath.Place(after, first.X, first.Y, first.Width, Content(after, first.Width, 40));

        Assert.AreEqual(12, second.X, 0.01);
        Assert.AreEqual(18, second.Y, 0.01);
    }

    [TestMethod]
    public void ShrinksOversizedWidth()
    {
        var monitor = Monitor("small", new(0, 0, 2560, 800), dpi: 100);

        var placement = DisplayPlacementMath.Place(monitor, 8, 8, 5000, Content(monitor, 5000, 40));

        Assert.AreEqual(2457, placement.Width);
        Assert.IsTrue(placement.WindowRect.Left >= monitor.Bounds.Left);
        Assert.IsTrue(placement.WindowRect.Right <= monitor.Bounds.Right);
        Assert.IsTrue(placement.WindowRect.Top >= monitor.Bounds.Top);
        Assert.IsTrue(placement.WindowRect.Bottom <= monitor.Bounds.Bottom);
    }

    [TestMethod]
    public void FindsNearestMonitorInEachDirection()
    {
        var current = Monitor("current", new(0, 0, 100, 100));
        var left = Monitor("left", new(-100, 0, 0, 100));
        var right = Monitor("right", new(100, 0, 200, 100));
        var up = Monitor("up", new(0, -100, 100, 0));
        var down = Monitor("down", new(0, 100, 100, 200));
        DisplayMonitor[] monitors = [current, left, right, up, down];

        Assert.AreSame(left, DisplayPlacementMath.FindDirectional(current, monitors, DisplayDirection.Left));
        Assert.AreSame(right, DisplayPlacementMath.FindDirectional(current, monitors, DisplayDirection.Right));
        Assert.AreSame(up, DisplayPlacementMath.FindDirectional(current, monitors, DisplayDirection.Up));
        Assert.AreSame(down, DisplayPlacementMath.FindDirectional(current, monitors, DisplayDirection.Down));
    }

    [TestMethod]
    [DataRow(@"\\.\DISPLAY1", 9, 1)]
    [DataRow(@"\\.\DISPLAY27", 9, 27)]
    [DataRow("unknown", 9, 9)]
    public void ReadsDisplayNumberWithFallback(string deviceName, int fallback, int expected)
    {
        Assert.AreEqual(expected, DisplayMonitorOptions.GetNumber(deviceName, fallback));
    }

    [TestMethod]
    public void FormatsMonitorLabels()
    {
        Assert.AreEqual("(1) GP27-FUS", DisplayMonitorOptions.FormatLabel(" GP27-FUS ", 1));
        Assert.AreEqual("(2) Monitor", DisplayMonitorOptions.FormatLabel(null, 2));
        Assert.AreEqual("(2) GP27-FUS — unavailable", DisplayMonitorOptions.FormatLabel("GP27-FUS", 2, false));
        Assert.AreEqual("Monitor — unavailable", DisplayMonitorOptions.FormatLabel(null, null, false));
    }

    [TestMethod]
    public void SortsOptionsAndIncludesMissingPreferredMonitor()
    {
        var primary = new DisplayMonitorOption("primary", 1, "Primary", true);
        var secondary = new DisplayMonitorOption("secondary", 2, "Secondary", false);
        var knownPreferred = new DisplayMonitorOption("preferred", 3, "Preferred", false);

        var options = DisplayMonitorOptions.IncludePreferred(
            [secondary, primary],
            "PREFERRED",
            knownPreferred);

        CollectionAssert.AreEqual(new[] { "primary", "secondary", "preferred" }, options.Select(option => option.Id).ToArray());
        Assert.IsFalse(options[2].IsAvailable);
        Assert.AreEqual("(3) Preferred — unavailable", options[2].Label);
        Assert.AreSame(options[2], DisplayMonitorOptions.ResolveSelected("preferred", options));
    }

    [TestMethod]
    public void CreatesGenericUnavailableOptionWhenPreferredMonitorWasNeverObserved()
    {
        var options = DisplayMonitorOptions.IncludePreferred(
            [new DisplayMonitorOption("primary", 1, "Primary", true)],
            "missing",
            null);

        Assert.AreEqual("Monitor — unavailable", options[1].Label);
        Assert.IsFalse(options[1].IsAvailable);
    }

    private static DisplayMonitor Monitor(
        string id,
        PixelRect bounds,
        bool primary = false,
        uint dpi = 96) =>
        new(0, id, @"\\.\DISPLAY1", bounds, primary, dpi);

    private static PixelRect Content(DisplayMonitor monitor, double width, double height)
    {
        var scale = monitor.Dpi / 96.0;
        return new(
            0,
            0,
            (int)Math.Round(width * scale, MidpointRounding.AwayFromZero),
            (int)Math.Round(height * scale, MidpointRounding.AwayFromZero));
    }
}
