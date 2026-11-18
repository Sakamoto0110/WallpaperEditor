using WallpaperEditor.Core.Monitors;

namespace WallpaperEditor.Core.Wallpapers;

/// <summary>
/// How an image is turned before it is fitted: rotated clockwise in quarter turns, then mirrored. The mirrors act on
/// the image as already rotated, so "mirror horizontally" always swaps left and right of what the user sees.
/// </summary>
/// <param name="Rotation">Degrees clockwise. Only quarter turns count; anything else is rounded down to one.</param>
/// <param name="FlipHorizontal">Swaps left and right.</param>
/// <param name="FlipVertical">Swaps top and bottom.</param>
public readonly record struct ImageOrientation(int Rotation = 0, bool FlipHorizontal = false, bool FlipVertical = false)
{
    public static ImageOrientation None => default;

    /// <summary>The rotation as 0, 90, 180 or 270.</summary>
    public int Degrees => (((Rotation / 90) % 4) + 4) % 4 * 90;

    public bool IsNone => Degrees == 0 && !FlipHorizontal && !FlipVertical;

    /// <summary>A quarter turn exchanges width and height.</summary>
    public bool SwapsAxes => Degrees is 90 or 270;

    /// <summary>The size of the image once turned.</summary>
    public PixelSize Apply(PixelSize size) => SwapsAxes ? new PixelSize(size.Height, size.Width) : size;

    /// <summary>
    /// Turns what the user sees by <paramref name="degrees"/> clockwise. Behind a single mirror, the stored rotation
    /// has to go the other way for the picture to turn the way the button says.
    /// </summary>
    public ImageOrientation Rotated(int degrees)
    {
        var mirrored = FlipHorizontal != FlipVertical;
        return this with { Rotation = Normalize(Degrees + (mirrored ? -degrees : degrees)) };
    }

    public ImageOrientation MirroredHorizontally() => this with { FlipHorizontal = !FlipHorizontal };

    public ImageOrientation MirroredVertically() => this with { FlipVertical = !FlipVertical };

    private static int Normalize(int degrees) => ((((degrees / 90) % 4) + 4) % 4) * 90;
}
