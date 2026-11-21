namespace OneUI.Core.Services;

/// <summary>
/// Computes smooth macOS-style cosine/bell-curve magnification scale factors for dock icons.
/// </summary>
public static class MagnificationMath
{
    /// <summary>
    /// Computes the icon scale factor in [1.0, maxScale] given horizontal distance from the cursor.
    /// </summary>
    /// <param name="distancePx">Absolute horizontal distance in DIPs from cursor to icon center.</param>
    /// <param name="influenceRadiusPx">Radius beyond which magnification is 1.0.</param>
    /// <param name="maxScale">Peak scale when cursor is directly over the icon center.</param>
    public static double ComputeScale(double distancePx, double influenceRadiusPx, double maxScale)
    {
        if (maxScale <= 1.0 || influenceRadiusPx <= 0)
            return 1.0;

        var absDist = Math.Abs(distancePx);
        if (absDist >= influenceRadiusPx)
            return 1.0;

        // Smooth raised-cosine bell curve: 0.5 * (1 + cos(pi * d / R))
        var normalized = absDist / influenceRadiusPx;
        var weight = 0.5 * (1.0 + Math.Cos(Math.PI * normalized));
        return 1.0 + (maxScale - 1.0) * weight;
    }

    /// <summary>
    /// Computes vertical lift (negative Y offset) proportional to the magnification factor.
    /// </summary>
    public static double ComputeLiftOffset(double scale, double maxLiftPx = 10.0)
    {
        if (scale <= 1.0 || maxLiftPx <= 0)
            return 0.0;

        return -Math.Min(maxLiftPx, (scale - 1.0) * maxLiftPx * 1.6);
    }
}
