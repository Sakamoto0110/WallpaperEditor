using System.Windows;
using System.Windows.Media;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Rendering;

/// <summary>Draws an image turned by an <see cref="ImageOrientation"/> into a placement computed for the turned size.</summary>
internal static class OrientedDrawing
{
    /// <summary>The size to decode at: the placement's, before the turn.</summary>
    public static (double Width, double Height) DecodeSize(Placement placement, ImageOrientation orientation) =>
        orientation.SwapsAxes ? (placement.Height, placement.Width) : (placement.Width, placement.Height);

    public static void Draw(DrawingContext context, ImageSource image, Placement placement, ImageOrientation orientation)
    {
        var area = new Rect(placement.X, placement.Y, placement.Width, placement.Height);
        if (orientation.IsNone)
        {
            context.DrawImage(image, area);
            return;
        }

        var (width, height) = DecodeSize(placement, orientation);

        // Pushed transforms apply innermost first: the image is rotated around its center, then mirrored,
        // then moved to the center of its placement.
        context.PushTransform(new TranslateTransform(placement.X + (placement.Width / 2), placement.Y + (placement.Height / 2)));
        context.PushTransform(new ScaleTransform(orientation.FlipHorizontal ? -1 : 1, orientation.FlipVertical ? -1 : 1));
        context.PushTransform(new RotateTransform(orientation.Degrees));
        context.DrawImage(image, new Rect(-width / 2, -height / 2, width, height));
        context.Pop();
        context.Pop();
        context.Pop();
    }
}
