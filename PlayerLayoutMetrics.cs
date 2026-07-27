namespace WPlayer;

internal readonly record struct PlayerLayoutMetrics(double Scale)
{
    public double RootPadding => 2 * Scale;
    public double RootCornerRadius => 4 * Scale;
    public double ControlCornerRadius => 3 * Scale;
    public double FontSize => 13 * Scale;
    public double RowHeight => 22 * Scale;
    public double PreviousNextButtonWidth => 21 * Scale;
    public double PlayPauseButtonWidth => 28 * Scale;
    public double ControlGap => 3 * Scale;
    public double PreviousNextIconSize => 8 * Scale;
    public double PlayPauseIconSize => 11 * Scale;
    public double ContentHorizontalInset => 4 * Scale;
    public double VolumeIndicatorWidth => 22 * Scale;
    public double VolumeIconSize => 18 * Scale;
    public double AppIconSize => 16 * Scale;
    public double AppIconGap => 5 * Scale;
    public double UtilityButtonWidth => 18 * Scale;
    public double CycleIconWidth => 8 * Scale;
    public double CycleIconHeight => 13 * Scale;
    public double DragIconWidth => 7 * Scale;
    public double DragIconHeight => 11 * Scale;
    public double ResizeCueSize => 9 * Scale;
    public double ResizeHitTargetWidth => 6 * Scale;
    public double ResizeHitTargetOutsideWidth => 5 * Scale;

    public static double CalculateLineHeight(
        double minimumRowHeight,
        double fontLineSpacing,
        double fontSize,
        double dpiScale)
    {
        var rowPixels = (int)Math.Round(minimumRowHeight * dpiScale, MidpointRounding.AwayFromZero);
        var linePixels = (int)Math.Ceiling(fontLineSpacing * fontSize * dpiScale);
        rowPixels = Math.Max(rowPixels, linePixels);
        if ((rowPixels - linePixels) % 2 != 0)
        {
            linePixels++;
        }

        return linePixels / dpiScale;
    }
}
