namespace WallpaperEditor.Core.Projects;

public readonly record struct CanvasPoint(double X, double Y);

/// <summary>A resized rectangle, and whether the drag went past the opposite side in either direction.</summary>
/// <param name="Rect">The new rectangle.</param>
/// <param name="FlippedX">The item's left and right swapped places.</param>
/// <param name="FlippedY">The item's top and bottom swapped places.</param>
public readonly record struct ResizeResult(ItemRect Rect, bool FlippedX, bool FlippedY);

/// <summary>A corner of an item, in the item's own, unrotated terms.</summary>
public enum ItemCorner
{
    TopLeft,
    TopRight,
    BottomRight,
    BottomLeft,
}

/// <summary>The middle of a side of an item, in the item's own, unrotated terms.</summary>
public enum ItemEdge
{
    Top,
    Right,
    Bottom,
    Left,
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

    /// <summary>A canvas point in the item's own unrotated coordinates, measured from its top-left corner.</summary>
    public static CanvasPoint ToLocal(ItemRect rect, double rotation, CanvasPoint point)
    {
        var center = Center(rect);
        var local = Rotate(new CanvasPoint(point.X - center.X, point.Y - center.Y), -rotation);
        return new CanvasPoint(local.X + (rect.Width / 2), local.Y + (rect.Height / 2));
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
    public static ItemRect ResizeFromCorner(ItemRect rect, double rotation, ItemCorner corner, CanvasPoint pointer, bool keepAspect, double minimumSize) =>
        ResizeFromCorner(rect, rotation, corner, pointer, keepAspect, minimumSize, allowFlip: false).Rect;

    /// <summary>
    /// As the other overload, but with <paramref name="allowFlip"/> a drag past the opposite corner keeps going on
    /// the other side, the way a mirror image would, instead of stopping at the minimum size.
    /// </summary>
    public static ResizeResult ResizeFromCorner(ItemRect rect, double rotation, ItemCorner corner, CanvasPoint pointer, bool keepAspect, double minimumSize, bool allowFlip)
    {
        var anchor = CornerPoint(rect, rotation, Opposite(corner));
        var (sx, sy) = Signs(corner);

        var local = Rotate(new CanvasPoint(pointer.X - anchor.X, pointer.Y - anchor.Y), -rotation);
        var flipX = allowFlip && sx * local.X < 0;
        var flipY = allowFlip && sy * local.Y < 0;
        var width = Math.Max(minimumSize, flipX ? -sx * local.X : sx * local.X);
        var height = Math.Max(minimumSize, flipY ? -sy * local.Y : sy * local.Y);

        if (keepAspect && rect.Width > 0 && rect.Height > 0)
        {
            var scale = Math.Max(width / rect.Width, height / rect.Height);
            scale = Math.Max(scale, minimumSize / Math.Min(rect.Width, rect.Height));
            width = rect.Width * scale;
            height = rect.Height * scale;
        }

        return new ResizeResult(Around(anchor, rotation, flipX ? -sx : sx, flipY ? -sy : sy, width, height), flipX, flipY);
    }

    /// <summary>Where the middle of a side ends up on the canvas once the item is rotated.</summary>
    public static CanvasPoint EdgePoint(ItemRect rect, double rotation, ItemEdge edge)
    {
        var center = Center(rect);
        var (sx, sy) = Signs(edge);
        var offset = Rotate(new CanvasPoint(sx * rect.Width / 2, sy * rect.Height / 2), rotation);
        return new CanvasPoint(center.X + offset.X, center.Y + offset.Y);
    }

    /// <summary>
    /// The rectangle after dragging the middle of a side to <paramref name="pointer"/>: only the width or only the
    /// height changes, and the opposite side stays exactly where it is on the canvas.
    /// </summary>
    public static ItemRect ResizeFromEdge(ItemRect rect, double rotation, ItemEdge edge, CanvasPoint pointer, double minimumSize) =>
        ResizeFromEdge(rect, rotation, edge, pointer, minimumSize, allowFlip: false).Rect;

    /// <summary>As the other overload, but a drag past the opposite side may flip the item, as with a corner.</summary>
    public static ResizeResult ResizeFromEdge(ItemRect rect, double rotation, ItemEdge edge, CanvasPoint pointer, double minimumSize, bool allowFlip)
    {
        var anchor = EdgePoint(rect, rotation, Opposite(edge));
        var (sx, sy) = Signs(edge);

        var local = Rotate(new CanvasPoint(pointer.X - anchor.X, pointer.Y - anchor.Y), -rotation);
        var flipX = allowFlip && sx != 0 && sx * local.X < 0;
        var flipY = allowFlip && sy != 0 && sy * local.Y < 0;
        var width = sx != 0 ? Math.Max(minimumSize, Math.Abs(local.X)) : rect.Width;
        var height = sy != 0 ? Math.Max(minimumSize, Math.Abs(local.Y)) : rect.Height;
        if (!allowFlip)
        {
            width = sx != 0 ? Math.Max(minimumSize, sx * local.X) : width;
            height = sy != 0 ? Math.Max(minimumSize, sy * local.Y) : height;
        }

        return new ResizeResult(Around(anchor, rotation, flipX ? -sx : sx, flipY ? -sy : sy, width, height), flipX, flipY);
    }

    /// <summary>
    /// The side of the item whose line passes within <paramref name="tolerance"/> of the point, along its length.
    /// Null if none does.
    /// </summary>
    public static ItemEdge? EdgeAt(ItemRect rect, double rotation, CanvasPoint point, double tolerance)
    {
        var local = ToLocal(rect, rotation, point);
        var alongX = local.X >= -tolerance && local.X <= rect.Width + tolerance;
        var alongY = local.Y >= -tolerance && local.Y <= rect.Height + tolerance;

        if (alongY && Math.Abs(local.X) <= tolerance)
        {
            return ItemEdge.Left;
        }

        if (alongY && Math.Abs(local.X - rect.Width) <= tolerance)
        {
            return ItemEdge.Right;
        }

        if (alongX && Math.Abs(local.Y) <= tolerance)
        {
            return ItemEdge.Top;
        }

        return alongX && Math.Abs(local.Y - rect.Height) <= tolerance ? ItemEdge.Bottom : null;
    }

    /// <summary>The direction, in degrees on screen, in which a corner moves when dragged outward.</summary>
    public static double OutwardAngle(ItemCorner corner, double rotation)
    {
        var (sx, sy) = Signs(corner);
        return (Math.Atan2(sy, sx) * 180 / Math.PI) + rotation;
    }

    /// <summary>The direction, in degrees on screen, in which a side moves when dragged outward.</summary>
    public static double OutwardAngle(ItemEdge edge, double rotation)
    {
        var (sx, sy) = Signs(edge);
        return (Math.Atan2(sy, sx) * 180 / Math.PI) + rotation;
    }

    /// <summary>The rectangle of the given size that reaches from the anchor toward the given signs.</summary>
    private static ItemRect Around(CanvasPoint anchor, double rotation, int sx, int sy, double width, double height)
    {
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

    private static (int X, int Y) Signs(ItemEdge edge) => edge switch
    {
        ItemEdge.Top => (0, -1),
        ItemEdge.Right => (1, 0),
        ItemEdge.Bottom => (0, 1),
        ItemEdge.Left => (-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
    };

    private static ItemEdge Opposite(ItemEdge edge) => edge switch
    {
        ItemEdge.Top => ItemEdge.Bottom,
        ItemEdge.Right => ItemEdge.Left,
        ItemEdge.Bottom => ItemEdge.Top,
        ItemEdge.Left => ItemEdge.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
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
