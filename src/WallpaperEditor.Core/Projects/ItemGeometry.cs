namespace WallpaperEditor.Core.Projects;

public readonly record struct CanvasPoint(double X, double Y);

/// <summary>A corner of an item, in the item's own, unrotated terms.</summary>
public enum ItemCorner
{
    TopLeft,
    TopRight,
    BottomRight,
    BottomLeft,
}

/// <summary>The math of dragging rotated items around, kept apart from the UI so it can be tested.</summary>
public static class ItemGeometry
{
    private const double SnapDegrees = 3;

    public static CanvasPoint Center(ItemRect rect) => new(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

    public static bool Contains(ItemRect rect, double rotation, CanvasPoint point)
    {
        var center = Center(rect);
        var local = Rotate(new CanvasPoint(point.X - center.X, point.Y - center.Y), -rotation);
        return Math.Abs(local.X) <= rect.Width / 2 && Math.Abs(local.Y) <= rect.Height / 2;
    }

    /// <summary>Where a corner ends up on the canvas once the item is rotated.</summary>
    public static CanvasPoint CornerPoint(ItemRect rect, double rotation, ItemCorner corner)
    {
        var center = Center(rect);
        var (sx, sy) = Signs(corner);
        var offset = Rotate(new CanvasPoint(sx * rect.Width / 2, sy * rect.Height / 2), rotation);
        return new CanvasPoint(center.X + offset.X, center.Y + offset.Y);
    }

    /// <summary>Where the rotation handle sits: above the top edge, turning with the item.</summary>
    public static CanvasPoint RotationHandle(ItemRect rect, double rotation, double distance)
    {
        var center = Center(rect);
        var offset = Rotate(new CanvasPoint(0, -(rect.Height / 2) - distance), rotation);
        return new CanvasPoint(center.X + offset.X, center.Y + offset.Y);
    }

    /// <summary>
    /// The rectangle after dragging a corner to <paramref name="pointer"/>. The opposite corner stays exactly
    /// where it is on the canvas, which for a rotated item means the center has to move.
    /// </summary>
    public static ItemRect ResizeFromCorner(ItemRect rect, double rotation, ItemCorner corner, CanvasPoint pointer, bool keepAspect, double minimumSize)
    {
        var anchor = CornerPoint(rect, rotation, Opposite(corner));
        var (sx, sy) = Signs(corner);

        var local = Rotate(new CanvasPoint(pointer.X - anchor.X, pointer.Y - anchor.Y), -rotation);
        var width = Math.Max(minimumSize, sx * local.X);
        var height = Math.Max(minimumSize, sy * local.Y);

        if (keepAspect && rect.Width > 0 && rect.Height > 0)
        {
            var scale = Math.Max(width / rect.Width, height / rect.Height);
            scale = Math.Max(scale, minimumSize / Math.Min(rect.Width, rect.Height));
            width = rect.Width * scale;
            height = rect.Height * scale;
        }

        var toCenter = Rotate(new CanvasPoint(sx * width / 2, sy * height / 2), rotation);
        var centerX = anchor.X + toCenter.X;
        var centerY = anchor.Y + toCenter.Y;
        return new ItemRect(centerX - (width / 2), centerY - (height / 2), width, height);
    }

    /// <summary>The rotation that points the top of the item at the pointer, snapping to straight angles.</summary>
    public static double RotationToward(ItemRect rect, CanvasPoint pointer)
    {
        var center = Center(rect);
        var degrees = (Math.Atan2(pointer.Y - center.Y, pointer.X - center.X) * 180 / Math.PI) + 90;
        var normalized = ((degrees % 360) + 360) % 360;

        var nearest = Math.Round(normalized / 90) * 90;
        return Math.Abs(normalized - nearest) <= SnapDegrees ? nearest % 360 : normalized;
    }

    private static (int X, int Y) Signs(ItemCorner corner) => corner switch
    {
        ItemCorner.TopLeft => (-1, -1),
        ItemCorner.TopRight => (1, -1),
        ItemCorner.BottomRight => (1, 1),
        ItemCorner.BottomLeft => (-1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
    };

    private static ItemCorner Opposite(ItemCorner corner) => corner switch
    {
        ItemCorner.TopLeft => ItemCorner.BottomRight,
        ItemCorner.TopRight => ItemCorner.BottomLeft,
        ItemCorner.BottomRight => ItemCorner.TopLeft,
        ItemCorner.BottomLeft => ItemCorner.TopRight,
        _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null),
    };

    private static CanvasPoint Rotate(CanvasPoint point, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var (sin, cos) = Math.SinCos(radians);
        return new CanvasPoint((point.X * cos) - (point.Y * sin), (point.X * sin) + (point.Y * cos));
    }
}
