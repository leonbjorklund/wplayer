namespace WPlayer.Tests;

[TestClass]
public sealed class PlayerLayoutMetricsTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void ScalesEveryPlayerMetric(double scale)
    {
        var metrics = new PlayerLayoutMetrics(scale);

        Assert.AreEqual(2 * scale, metrics.RootPadding);
        Assert.AreEqual(4 * scale, metrics.RootCornerRadius);
        Assert.AreEqual(3 * scale, metrics.ControlCornerRadius);
        Assert.AreEqual(13 * scale, metrics.FontSize);
        Assert.AreEqual(22 * scale, metrics.RowHeight);
        Assert.AreEqual(21 * scale, metrics.PreviousNextButtonWidth);
        Assert.AreEqual(28 * scale, metrics.PlayPauseButtonWidth);
        Assert.AreEqual(3 * scale, metrics.ControlGap);
        Assert.AreEqual(8 * scale, metrics.PreviousNextIconSize);
        Assert.AreEqual(11 * scale, metrics.PlayPauseIconSize);
        Assert.AreEqual(21 * scale, metrics.TitlePlaybackLayoutWidth);
        Assert.AreEqual(4 * scale, metrics.ContentHorizontalInset);
        Assert.AreEqual(22 * scale, metrics.VolumeIndicatorWidth);
        Assert.AreEqual(18 * scale, metrics.VolumeIconSize);
        Assert.AreEqual(16 * scale, metrics.AppIconSize);
        Assert.AreEqual(5 * scale, metrics.AppIconGap);
        Assert.AreEqual(18 * scale, metrics.UtilityButtonWidth);
        Assert.AreEqual(8 * scale, metrics.CycleIconWidth);
        Assert.AreEqual(13 * scale, metrics.CycleIconHeight);
        Assert.AreEqual(7 * scale, metrics.DragIconWidth);
        Assert.AreEqual(11 * scale, metrics.DragIconHeight);
        Assert.AreEqual(9 * scale, metrics.ResizeCueSize);
        Assert.AreEqual(6 * scale, metrics.ResizeHitTargetWidth);
        Assert.AreEqual(5 * scale, metrics.ResizeHitTargetOutsideWidth);
        Assert.AreEqual(12 * scale, metrics.VolumePercentFontSize);
        Assert.AreEqual(-0.5 * scale, metrics.VolumePercentVerticalOffset);
        Assert.AreEqual(4 * scale, metrics.VolumeTitleClearance);
    }

    [TestMethod]
    [DataRow(22, 1.2, 13, 1, 16)]
    [DataRow(22, 1.2, 13, 1.5, 16.666666666666668)]
    [DataRow(22, 1.2, 40, 1.5, 48)]
    public void LineHeightFitsTextAndKeepsPixelParity(
        double minimumRowHeight,
        double fontLineSpacing,
        double fontSize,
        double dpiScale,
        double expected)
    {
        var lineHeight = PlayerLayoutMetrics.CalculateLineHeight(
            minimumRowHeight,
            fontLineSpacing,
            fontSize,
            dpiScale);

        Assert.AreEqual(expected, lineHeight, 0.0001);
    }
}
