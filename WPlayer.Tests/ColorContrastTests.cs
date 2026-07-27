using System.Windows.Media;

namespace WPlayer.Tests;

[TestClass]
public sealed class ColorContrastTests
{
    private static readonly Color LightForeground = Color.FromRgb(0xF1, 0xF3, 0xF4);

    [TestMethod]
    public void DefaultPaletteProducesApprovedColors()
    {
        var palette = ColorContrast.PaletteFromBackground(Color.FromRgb(0x1C, 0x1C, 0x1C));

        Assert.AreEqual(Color.FromRgb(0x31, 0x31, 0x31), palette.PlaybackBackground);
        Assert.AreEqual(Color.FromRgb(0x47, 0x47, 0x47), palette.PlaybackHoverBackground);
        Assert.AreEqual(Color.FromRgb(0x36, 0x36, 0x36), palette.UtilityHoverBackground);
        Assert.AreEqual(LightForeground, palette.PlaybackForeground);
        Assert.AreEqual(LightForeground, palette.UtilityForeground);
    }

    [TestMethod]
    public void DarkColoredBackgroundUsesDarkSurfaceAndUnifiedHoverRatios()
    {
        var background = Color.FromRgb(0x14, 0x2E, 0x40);
        var palette = ColorContrast.PaletteFromBackground(background);

        Assert.AreEqual(1.3, ColorContrast.GetContrastRatio(background, palette.PlaybackBackground), 0.02);
        Assert.AreEqual(1.4, ColorContrast.GetContrastRatio(palette.PlaybackBackground, palette.PlaybackHoverBackground), 0.02);
        Assert.AreEqual(1.4, ColorContrast.GetContrastRatio(background, palette.UtilityHoverBackground), 0.02);
        Assert.AreEqual(LightForeground, palette.PlaybackForeground);
        Assert.AreEqual(LightForeground, palette.UtilityForeground);
    }

    [TestMethod]
    public void LightBackgroundUsesLightSurfaceRatioAndBlackForegrounds()
    {
        var background = Color.FromRgb(0xF3, 0xF1, 0xEC);
        var palette = ColorContrast.PaletteFromBackground(background);

        Assert.AreEqual(1.4, ColorContrast.GetContrastRatio(background, palette.PlaybackBackground), 0.02);
        Assert.AreEqual(1.4, ColorContrast.GetContrastRatio(palette.PlaybackBackground, palette.PlaybackHoverBackground), 0.02);
        Assert.AreEqual(1.4, ColorContrast.GetContrastRatio(background, palette.UtilityHoverBackground), 0.02);
        Assert.AreEqual(Colors.Black, palette.PlaybackForeground);
        Assert.AreEqual(Colors.Black, palette.UtilityForeground);
    }

    [TestMethod]
    [DataRow(0xFF, 0x69, 0xB4, 0xD4, 0x57, 0x96)]
    [DataRow(0x00, 0x80, 0x80, 0x33, 0x9A, 0x9A)]
    [DataRow(0x00, 0x70, 0xE8, 0x00, 0x5A, 0xBB)]
    public void ColoredBackgroundsPreferLightPlaybackForeground(
        int backgroundRed,
        int backgroundGreen,
        int backgroundBlue,
        int playbackRed,
        int playbackGreen,
        int playbackBlue)
    {
        var palette = ColorContrast.PaletteFromBackground(Color.FromRgb(
            (byte)backgroundRed,
            (byte)backgroundGreen,
            (byte)backgroundBlue));

        Assert.AreEqual(
            Color.FromRgb((byte)playbackRed, (byte)playbackGreen, (byte)playbackBlue),
            palette.PlaybackBackground);
        Assert.AreEqual(LightForeground, palette.PlaybackForeground);
        Assert.IsTrue(ColorContrast.GetContrastRatio(
            palette.PlaybackBackground,
            palette.PlaybackForeground) >= 3);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(254)]
    public void AnyBackgroundTransparencyUsesDefaultVirtualBase(int alpha)
    {
        var expected = ColorContrast.PaletteFromBackground(Color.FromRgb(0x1C, 0x1C, 0x1C));
        var actual = ColorContrast.PaletteFromBackground(
            Color.FromArgb((byte)alpha, 0xF3, 0xF1, 0xEC));

        Assert.AreEqual(expected, actual);
    }
}
