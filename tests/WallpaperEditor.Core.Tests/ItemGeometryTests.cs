using WallpaperEditor.Core.Projects;

namespace WallpaperEditor.Core.Tests;

public class ItemGeometryTests
{
    private static readonly ItemRect Rect = new(100, 100, 200, 100);

    [Fact]
    public void A_point_inside_an_unrotated_item_is_contained()
    {
        Assert.True(ItemGeometry.Contains(Rect, 0, new CanvasPoint(110, 110)));
        Assert.False(ItemGeometry.Contains(Rect, 0, new CanvasPoint(90, 110)));
    }

    [Fact]
    public void Containment_follows_the_rotation()
    {
        // Turned a quarter around its center (200,150), the item now spans x 150-250 and y 50-250.
        Assert.True(ItemGeometry.Contains(Rect, 90, new CanvasPoint(200, 60)));
        Assert.False(ItemGeometry.Contains(Rect, 90, new CanvasPoint(110, 150)));
    }

    [Fact]
    public void Corners_turn_with_the_item()
    {
        var corner = ItemGeometry.CornerPoint(Rect, 90, ItemCorner.TopLeft);

        Assert.Equal(250, corner.X, 3);
        Assert.Equal(50, corner.Y, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(217)]
    public void Resizing_keeps_the_opposite_corner_where_it_was(double rotation)
    {
        var anchor = ItemGeometry.CornerPoint(Rect, rotation, ItemCorner.TopLeft);
        var handle = ItemGeometry.CornerPoint(Rect, rotation, ItemCorner.BottomRight);

        var resized = ItemGeometry.ResizeFromCorner(Rect, rotation, ItemCorner.BottomRight, new CanvasPoint(handle.X + 40, handle.Y + 25), keepAspect: false, minimumSize: 16);

        var anchorAfter = ItemGeometry.CornerPoint(resized, rotation, ItemCorner.TopLeft);
        Assert.Equal(anchor.X, anchorAfter.X, 3);
        Assert.Equal(anchor.Y, anchorAfter.Y, 3);
    }

    [Fact]
    public void Dragging_a_corner_of_an_unrotated_item_follows_the_pointer()
    {
        var resized = ItemGeometry.ResizeFromCorner(Rect, 0, ItemCorner.BottomRight, new CanvasPoint(350, 260), keepAspect: false, minimumSize: 16);

        Assert.Equal(100, resized.X, 3);
        Assert.Equal(100, resized.Y, 3);
        Assert.Equal(250, resized.Width, 3);
        Assert.Equal(160, resized.Height, 3);
    }

    [Theory]
    [InlineData(ItemCorner.TopLeft)]
    [InlineData(ItemCorner.TopRight)]
    [InlineData(ItemCorner.BottomRight)]
    [InlineData(ItemCorner.BottomLeft)]
    public void Keeping_the_aspect_preserves_the_proportions(ItemCorner corner)
    {
        var handle = ItemGeometry.CornerPoint(Rect, 0, corner);

        var resized = ItemGeometry.ResizeFromCorner(Rect, 0, corner, new CanvasPoint(handle.X + 77, handle.Y - 13), keepAspect: true, minimumSize: 16);

        Assert.Equal(Rect.Width / Rect.Height, resized.Width / resized.Height, 3);
    }

    [Fact]
    public void An_item_cannot_collapse_or_turn_inside_out()
    {
        var resized = ItemGeometry.ResizeFromCorner(Rect, 0, ItemCorner.BottomRight, new CanvasPoint(-500, -500), keepAspect: false, minimumSize: 16);

        Assert.Equal(16, resized.Width, 3);
        Assert.Equal(16, resized.Height, 3);
        Assert.Equal(100, resized.X, 3);
        Assert.Equal(100, resized.Y, 3);
    }

    [Fact]
    public void Keeping_the_aspect_still_respects_the_minimum_on_the_short_side()
    {
        var resized = ItemGeometry.ResizeFromCorner(Rect, 0, ItemCorner.BottomRight, new CanvasPoint(-500, -500), keepAspect: true, minimumSize: 16);

        Assert.Equal(16, resized.Height, 3);
        Assert.Equal(32, resized.Width, 3);
    }

    [Theory]
    [InlineData(200, 0, 0)]
    [InlineData(400, 150, 90)]
    [InlineData(200, 300, 180)]
    [InlineData(0, 150, 270)]
    public void Rotation_points_the_top_of_the_item_at_the_pointer(double x, double y, double expected)
    {
        Assert.Equal(expected, ItemGeometry.RotationToward(Rect, new CanvasPoint(x, y)), 3);
    }

    [Fact]
    public void Rotation_snaps_to_straight_angles_when_close()
    {
        // About 1 degree off vertical.
        Assert.Equal(0, ItemGeometry.RotationToward(Rect, new CanvasPoint(202, 0)), 3);
    }

    [Fact]
    public void Rotation_does_not_snap_when_clearly_angled()
    {
        var angle = ItemGeometry.RotationToward(Rect, new CanvasPoint(300, 50));

        Assert.Equal(45, angle, 3);
    }
}
