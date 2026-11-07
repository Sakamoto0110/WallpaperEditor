using WallpaperEditor.Core.Monitors;

namespace WallpaperEditor.Core.Wallpapers;

/// <summary>
/// How an image is adapted to a monitor. The app applies this itself when rendering, because the
/// Windows setting of the same name is global and would change every monitor at once.
/// </summary>
public enum FitMode
{
    /// <summary>Covers the whole monitor, cropping what does not fit.</summary>
    Fill,

    /// <summary>Shows the whole image, leaving bars where it does not reach.</summary>
    Fit,

    /// <summary>Keeps the original size, in the middle of the monitor.</summary>
    Center,

    /// <summary>Covers the whole monitor, distorting the image if the proportions differ.</summary>
    Stretch,
}

/// <summary>Where an image lands on the target, in target pixels. May extend past the edges.</summary>
public readonly record struct Placement(double X, double Y, double Width, double Height);

public static class FitCalculator
{
    public static Placement Compute(PixelSize source, PixelSize target, FitMode mode)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "The image has no pixels.");
        }

        if (target.Width <= 0 || target.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "The target has no pixels.");
        }

        var horizontal = (double)target.Width / source.Width;
        var vertical = (double)target.Height / source.Height;

        return mode switch
        {
            FitMode.Fill => Centered(source, target, Math.Max(horizontal, vertical)),
            FitMode.Fit => Centered(source, target, Math.Min(horizontal, vertical)),
            FitMode.Center => OnPixelGrid(Centered(source, target, 1)),
            FitMode.Stretch => new Placement(0, 0, target.Width, target.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    /// <summary>An odd difference in size would otherwise land half a pixel off, and blur the whole image.</summary>
    private static Placement OnPixelGrid(Placement placement) =>
        placement with { X = Math.Floor(placement.X), Y = Math.Floor(placement.Y) };

    private static Placement Centered(PixelSize source, PixelSize target, double scale)
    {
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new Placement((target.Width - width) / 2, (target.Height - height) / 2, width, height);
    }
}
