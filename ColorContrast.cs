using System.Windows.Media;

namespace WPlayer;

internal static class ColorContrast
{
    private const double DarkPlaybackContrastRatio = 1.3;
    private const double LightPlaybackContrastRatio = 1.4;
    private const double HoverContrastRatio = 1.4;
    private static readonly Color DefaultBackground = Color.FromRgb(0x1C, 0x1C, 0x1C);
    private static readonly Color LightForeground = Color.FromRgb(0xF1, 0xF3, 0xF4);

    public static (
        Color PlaybackBackground,
        Color PlaybackForeground,
        Color PlaybackHoverBackground,
        Color UtilityForeground,
        Color UtilityHoverBackground) PaletteFromBackground(Color background)
    {
        var effectiveBackground = background.A < byte.MaxValue ? DefaultBackground : background;
        var utilityForeground = GetContrastRatio(effectiveBackground, LightForeground) >= 3
            ? LightForeground
            : ForegroundFromBackground(effectiveBackground);
        var playbackContrastRatio = ForegroundFromBackground(effectiveBackground) == Colors.Black
            ? LightPlaybackContrastRatio
            : DarkPlaybackContrastRatio;
        var playbackBackground = ColorAtContrast(effectiveBackground, playbackContrastRatio);
        if (GetContrastRatio(playbackBackground, LightForeground) < 3)
        {
            var endpoint = ContrastEndpoint(effectiveBackground) == Colors.White ? Colors.Black : Colors.White;
            var alternateBackground = ColorAtContrast(effectiveBackground, playbackContrastRatio, endpoint);
            if (GetContrastRatio(alternateBackground, LightForeground) >= 3)
            {
                playbackBackground = alternateBackground;
            }
        }
        var playbackForeground = GetContrastRatio(playbackBackground, LightForeground) >= 3
            ? LightForeground
            : ForegroundFromBackground(playbackBackground);

        return (
            playbackBackground,
            playbackForeground,
            ColorAtContrast(playbackBackground, HoverContrastRatio),
            utilityForeground,
            ColorAtContrast(effectiveBackground, HoverContrastRatio));
    }

    internal static Color ColorAtContrast(Color background, double targetContrastRatio)
        => ColorAtContrast(background, targetContrastRatio, ContrastEndpoint(background));

    private static Color ColorAtContrast(Color background, double targetContrastRatio, Color endpoint)
    {
        var low = 0d;
        var high = 1d;

        for (var i = 0; i < 40; i++)
        {
            var amount = (low + high) / 2;
            if (GetContrastRatio(background, endpoint, amount) < targetContrastRatio)
            {
                low = amount;
            }
            else
            {
                high = amount;
            }
        }

        return Color.FromRgb(
            Mix(background.R, endpoint.R, high),
            Mix(background.G, endpoint.G, high),
            Mix(background.B, endpoint.B, high));
    }

    private static Color ContrastEndpoint(Color background) =>
        GetContrastRatio(background, Colors.White) >= GetContrastRatio(background, Colors.Black)
            ? Colors.White
            : Colors.Black;

    private static Color ForegroundFromBackground(Color background) =>
        GetContrastRatio(background, LightForeground) >= GetContrastRatio(background, Colors.Black)
            ? LightForeground
            : Colors.Black;

    internal static double GetContrastRatio(Color first, Color second)
    {
        var lighter = Math.Max(GetRelativeLuminance(first), GetRelativeLuminance(second));
        var darker = Math.Min(GetRelativeLuminance(first), GetRelativeLuminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double GetContrastRatio(Color background, Color endpoint, double amount) =>
        GetContrastRatio(background, Color.FromRgb(
            Mix(background.R, endpoint.R, amount),
            Mix(background.G, endpoint.G, amount),
            Mix(background.B, endpoint.B, amount)));

    private static double GetRelativeLuminance(Color color) =>
        0.2126 * ToLinear(color.R) + 0.7152 * ToLinear(color.G) + 0.0722 * ToLinear(color.B);

    private static double ToLinear(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static byte Mix(byte start, byte end, double amount) =>
        (byte)Math.Round(start + (end - start) * amount, MidpointRounding.AwayFromZero);
}
