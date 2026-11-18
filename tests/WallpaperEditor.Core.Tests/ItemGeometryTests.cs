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

    [Fact]
    public void Dragging_the_right_side_changes_only_the_width_and_keeps_the_left_side()
    {
        var resized = ItemGeometry.ResizeFromEdge(Rect, 0, ItemEdge.Right, new CanvasPoint(350, 400), 16);

        Assert.Equal(new ItemRect(100, 100, 250, 100), resized);
    }

    [Fact]
    public void Dragging_the_top_side_changes_only_the_height_and_keeps_the_bottom()
    {
        var resized = ItemGeometry.ResizeFromEdge(Rect, 0, ItemEdge.Top, new CanvasPoint(0, 50), 16);

        Assert.Equal(new ItemRect(100, 50, 200, 150), resized);
    }

    [Fact]
    public void On_a_rotated_item_the_opposite_side_stays_where_it_is()
    {
        // Turned a quarter around (200,150): the item's bottom side is now on the left of the canvas, at x=150.
        var before = ItemGeometry.EdgePoint(Rect, 90, ItemEdge.Top);
        var dragged = new CanvasPoint(before.X + 40, before.Y);

        var resized = ItemGeometry.ResizeFromEdge(Rect, 90, ItemEdge.Top, dragged, 16);
        var bottom = ItemGeometry.EdgePoint(resized, 90, ItemEdge.Bottom);

        Assert.Equal(150, bottom.X, 3);
        Assert.Equal(150, bottom.Y, 3);
        Assert.Equal(140, resized.Height, 3);
        Assert.Equal(200, resized.Width, 3);
    }

    [Fact]
    public void A_side_cannot_be_dragged_past_the_minimum_size()
    {
        var resized = ItemGeometry.ResizeFromEdge(Rect, 0, ItemEdge.Left, new CanvasPoint(500, 0), 16);

        Assert.Equal(16, resized.Width);
        Assert.Equal(300 - 16, resized.X);
    }

    [Fact]
    public void A_side_dragged_past_the_other_flips_and_keeps_growing_beyond_it()
    {
        // The right side of the item spans x 100-300; dragged to x 40, it passes the left side by 60.
        var result = ItemGeometry.ResizeFromEdge(Rect, 0, ItemEdge.Right, new CanvasPoint(40, 0), 16, allowFlip: true);

        Assert.True(result.FlippedX);
        Assert.False(result.FlippedY);
        Assert.Equal(new ItemRect(40, 100, 60, 100), result.Rect);
    }

    [Fact]
    public void A_side_that_stays_on_its_own_side_does_not_flip()
    {
        var result = ItemGeometry.ResizeFromEdge(Rect, 0, ItemEdge.Right, new CanvasPoint(250, 0), 16, allowFlip: true);

        Assert.False(result.FlippedX);
        Assert.Equal(new ItemRect(100, 100, 150, 100), result.Rect);
    }

    [Fact]
    public void A_corner_dragged_past_the_opposite_corner_flips_both_ways()
    {
        // The bottom-right corner, dragged up and left past the top-left one at (100,100).
        var result = ItemGeometry.ResizeFromCorner(Rect, 0, ItemCorner.BottomRight, new CanvasPoint(20, 50), keepAspect: false, 16, allowFlip: true);

        Assert.True(result.FlippedX);
        Assert.True(result.FlippedY);
        Assert.Equal(new ItemRect(20, 50, 80, 50), result.Rect);
    }

    [Fact]
    public void Without_flipping_a_corner_still_stops_at_the_minimum_size()
    {
        var result = ItemGeometry.ResizeFromCorner(Rect, 0, ItemCorner.BottomRight, new CanvasPoint(20, 50), keepAspect: false, 16, allowFlip: false);

        Assert.False(result.FlippedX || result.FlippedY);
        Assert.Equal(new ItemRect(100, 100, 16, 16), result.Rect);
    }

    [Theory]
    [InlineData(100, 150, ItemEdge.Left)]
    [InlineData(298, 120, ItemEdge.Right)]
    [InlineData(250, 103, ItemEdge.Top)]
    [InlineData(150, 201, ItemEdge.Bottom)]
    public void A_side_can_be_grabbed_anywhere_along_its_line(double x, double y, ItemEdge expected) =>
        Assert.Equal(expected, ItemGeometry.EdgeAt(Rect, 0, new CanvasPoint(x, y), 5));

    [Fact]
    public void The_middle_of_an_item_is_no_side() =>
        Assert.Null(ItemGeometry.EdgeAt(Rect, 0, new CanvasPoint(200, 150), 5));

    [Theory]
    [InlineData(ItemCorner.BottomRight, 0, 45)]
    [InlineData(ItemCorner.TopRight, 0, -45)]
    [InlineData(ItemCorner.BottomRight, 45, 90)]
    public void A_corner_moves_outward_diagonally_turned_with_the_item(ItemCorner corner, double rotation, double expected) =>
        Assert.Equal(expected, ItemGeometry.OutwardAngle(corner, rotation), precision: 6);

    [Fact]
    public void A_point_on_an_unrotated_item_is_measured_from_its_corner()
    {
        var local = ItemGeometry.ToLocal(Rect, 0, new CanvasPoint(130, 160));

        Assert.Equal(30, local.X, 3);
        Assert.Equal(60, local.Y, 3);
    }

    [Fact]
    public void A_click_on_a_rotated_item_lands_where_it_looks()
    {
        // Turned a quarter around (200,150), the item's top-left corner sits at (250,50) on the canvas.
        var corner = ItemGeometry.ToLocal(Rect, 90, new CanvasPoint(250, 50));
        var center = ItemGeometry.ToLocal(Rect, 90, new CanvasPoint(200, 150));

        Assert.Equal(0, corner.X, 3);
        Assert.Equal(0, corner.Y, 3);
        Assert.Equal(100, center.X, 3);
        Assert.Equal(50, center.Y, 3);
    }
}
