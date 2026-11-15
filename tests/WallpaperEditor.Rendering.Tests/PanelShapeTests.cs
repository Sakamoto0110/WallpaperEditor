using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperEditor.Core.Projects;

namespace WallpaperEditor.Rendering.Tests;

/// <summary>Rounded panels, speech balloons and thought balloons.</summary>
public sealed class PanelShapeTests
{
    private const int Width = 400;

    /// <summary>Red at half strength, on black: easy to tell apart from the background and to compare.</summary>
    private static readonly TextStyle Red = TextStyle.Default with { Color = "#00FFFFFF", Panel = "#80FF0000" };

    [Theory]
    [InlineData(PanelShape.Speech)]
    [InlineData(PanelShape.Thought)]
    public void A_balloon_needs_room_for_its_tail(PanelShape shape)
    {
        var rounded = Text(Red);
        var balloon = Text(Red with { Shape = shape });

        Assert.True(StyledItemPainter.RequiredHeight(balloon) > StyledItemPainter.RequiredHeight(rounded));
    }

    [Theory]
    [InlineData(TailSide.Left)]
    [InlineData(TailSide.Right)]
    public void The_tail_of_a_speech_balloon_points_to_the_chosen_side(TailSide side)
    {
        var item = Text(Red with { Shape = PanelShape.Speech, Tail = side });
        var height = StyledItemPainter.RequiredHeight(item);
        var image = Draw(item, height);

        // Below the body, where only the tail reaches: it narrows from x 80-120 at the body to a tip at x 67.
        var y = (int)(height - (Red.Size * 2) + (Red.Size * 0.5));
        var x = (int)(Width * 0.2) - 2;
        var left = Red_(image, x, y);
        var right = Red_(image, Width - x, y);

        Assert.True(side == TailSide.Left ? left && !right : right && !left, $"left={left} right={right}");
    }

    [Theory]
    [InlineData(TailSide.Left)]
    [InlineData(TailSide.Right)]
    public void A_thought_balloon_trails_off_to_the_chosen_side(TailSide side)
    {
        var item = Text(Red with { Shape = PanelShape.Thought, Tail = side });
        var height = StyledItemPainter.RequiredHeight(item);
        var image = Draw(item, height);

        // The first and largest circle below the cloud.
        var bodyBottom = height - (Red.Size * 2.6);
        var y = (int)(bodyBottom + (Red.Size * 0.55));
        var left = Red_(image, (int)(Width * 0.22), y);
        var right = Red_(image, Width - (int)(Width * 0.22), y);

        Assert.True(side == TailSide.Left ? left && !right : right && !left, $"left={left} right={right}");
    }

    [Fact]
    public void Where_the_parts_of_a_cloud_overlap_it_is_not_darker()
    {
        var item = Text(Red with { Shape = PanelShape.Thought }) with { Rect = new ItemRect(0, 0, Width, 300) };
        var image = Draw(item, 300);
        var core = Pixel(image, Width / 2, 120);

        // Puffs overlap the ellipse and each other all around the outline. Anywhere they did not merge, the red
        // would be stronger than in the middle. Edges can only be weaker, from antialiasing.
        var strongest = 0;
        for (var y = 0; y < 300; y += 2)
        {
            for (var x = 0; x < Width; x += 2)
            {
                strongest = Math.Max(strongest, Pixel(image, x, y).R);
            }
        }

        Assert.InRange(core.R, 110, 145);
        Assert.Equal(core.R, strongest);
    }

    [Fact]
    public void A_rounded_panel_looks_as_it_did_before_shapes_existed()
    {
        var item = Text(Red);
        var image = Draw(item, 120);

        Assert.InRange(Pixel(image, Width / 2, 60).R, 110, 145);
        Assert.Equal(0, Pixel(image, 0, 0).R);
    }

    [Fact]
    public void In_a_cloud_the_content_moves_in_and_the_clicks_move_with_it()
    {
        var counter = new CounterItem { Id = "n", Rect = new ItemRect(0, 0, Width, 200), Label = "Dias", Style = Red };
        var cloud = counter with { Style = Red with { Shape = PanelShape.Thought } };

        var plain = StyledItemPainter.Hotspots(counter, Width).Single(h => h.Action == HotspotAction.Increment).Area;
        var inCloud = StyledItemPainter.Hotspots(cloud, Width).Single(h => h.Action == HotspotAction.Increment).Area;

        Assert.True(inCloud.Top > plain.Top, "The content starts lower in a cloud.");
        Assert.True(inCloud.Right < plain.Right, "And further in from the right edge.");
    }

    [Fact]
    public void In_a_cloud_taller_than_needed_the_content_stays_in_the_middle()
    {
        var counter = new CounterItem { Id = "n", Rect = new ItemRect(0, 0, Width, 200), Label = "Dias", Style = Red with { Shape = PanelShape.Thought } };
        var snug = counter with { Rect = counter.Rect with { Height = StyledItemPainter.RequiredHeight(counter) } };
        var tall = snug with { Rect = snug.Rect with { Height = snug.Rect.Height + 200 } };

        var snugButton = StyledItemPainter.Hotspots(snug, Width).Single(h => h.Action == HotspotAction.Increment).Area;
        var tallButton = StyledItemPainter.Hotspots(tall, Width).Single(h => h.Action == HotspotAction.Increment).Area;

        // 200 more of height: the content moves down by half of it, and the clicks with it.
        Assert.Equal(snugButton.Top + 100, tallButton.Top, 3);
    }

    [Fact]
    public void Text_in_a_cloud_is_centered_and_elsewhere_starts_on_the_left()
    {
        var white = Red with { Color = "#FFFFFFFF", Panel = "#00000000" };
        var plain = Text(white) with { Text = "i" };
        var cloud = plain with { Style = white with { Shape = PanelShape.Thought } };

        Assert.True(LeftmostBright(Draw(plain, 120)) < Width / 4, "Without a cloud, the text starts at the left.");
        Assert.True(LeftmostBright(Draw(cloud, StyledItemPainter.RequiredHeight(cloud))) > Width / 3, "In a cloud, a short line sits in the middle.");
    }

    [Fact]
    public void A_chosen_alignment_wins_over_the_automatic_one_in_a_cloud()
    {
        var white = Red with { Color = "#FFFFFFFF", Panel = "#00000000", Shape = PanelShape.Thought };
        var left = Text(white) with { Text = "i", Align = TextAlign.Left };

        Assert.True(LeftmostBright(Draw(left, StyledItemPainter.RequiredHeight(left))) < Width / 3);
    }

    [Fact]
    public void Right_aligned_text_ends_at_the_right()
    {
        var white = Red with { Color = "#FFFFFFFF", Panel = "#00000000" };
        var right = Text(white) with { Text = "i", Align = TextAlign.Right };

        Assert.True(LeftmostBright(Draw(right, 120)) > Width * 3 / 4);
    }

    [Fact]
    public void Justified_lines_reach_further_right_than_left_aligned_ones()
    {
        var white = Red with { Color = "#FFFFFFFF", Panel = "#00000000" };
        var words = string.Join(' ', Enumerable.Repeat("palavra", 20));
        var left = Text(white) with { Text = words, Rect = new ItemRect(0, 0, Width, 300) };
        var justified = left with { Align = TextAlign.Justify };

        // The first line only: the last line of a justified paragraph stays on the left.
        Assert.True(RightmostBright(Draw(justified, 300), 18, 34) > RightmostBright(Draw(left, 300), 18, 34));
    }

    [Fact]
    public void Bold_text_needs_more_room_than_regular()
    {
        var white = Red with { Color = "#FFFFFFFF", Panel = "#00000000" };
        var words = Text(white) with { Text = string.Join(' ', Enumerable.Repeat("palavra", 30)), Rect = new ItemRect(0, 0, Width, 100) };

        Assert.True(StyledItemPainter.RequiredHeight(words with { Bold = true }) >= StyledItemPainter.RequiredHeight(words));
        Assert.NotEqual(Pixels(Draw(words, 300)), Pixels(Draw(words with { Italic = true }, 300)));
    }

    private static int RightmostBright(BitmapSource image, int top, int bottom)
    {
        for (var x = image.PixelWidth - 1; x >= 0; x--)
        {
            for (var y = top; y < bottom; y++)
            {
                if (Pixel(image, x, y).G > 120)
                {
                    return x;
                }
            }
        }

        return -1;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    private static int LeftmostBright(BitmapSource image)
    {
        for (var x = 0; x < image.PixelWidth; x++)
        {
            for (var y = 0; y < image.PixelHeight; y++)
            {
                if (Pixel(image, x, y).G > 120)
                {
                    return x;
                }
            }
        }

        return int.MaxValue;
    }

    private static TextItem Text(TextStyle style) => new()
    {
        Id = "t",
        Rect = new ItemRect(0, 0, Width, 120),
        Text = "Olá",
        Style = style,
    };

    private static BitmapSource Draw(StyledItem item, double height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, Width, height));
            StyledItemPainter.Draw(context, item, Width, height);
        }

        var bitmap = new RenderTargetBitmap(Width, (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool Red_(BitmapSource image, int x, int y) => Pixel(image, x, y).R > 60;

    private static Color Pixel(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }
}
