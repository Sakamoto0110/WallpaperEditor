using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperEditor.Core.Projects;

namespace WallpaperEditor.Rendering.Tests;

/// <summary>Where controls can be clicked in the editor, and what of them reaches the wallpaper.</summary>
public sealed class ControlRenderingTests
{
    private const int Width = 400;

    private static readonly TextStyle Style = TextStyle.Default with { Panel = "#00000000" };

    [Fact]
    public void Every_task_can_be_clicked_and_is_identified_by_its_entry()
    {
        var list = new ChecklistItem
        {
            Id = "c",
            Rect = new ItemRect(0, 0, Width, 300),
            Title = "Hoje",
            Entries = [new("a", "Primeira", false), new("b", "Segunda", true)],
            Style = Style,
        };

        var hotspots = StyledItemPainter.Hotspots(list, Width);

        Assert.Equal(["a", "b"], hotspots.Select(h => h.EntryId));
        Assert.All(hotspots, h => Assert.Equal(HotspotAction.ToggleEntry, h.Action));
        Assert.True(hotspots[1].Area.Top > hotspots[0].Area.Top, "The second task is below the first.");
    }

    [Fact]
    public void The_title_of_a_checklist_is_not_a_task()
    {
        var list = new ChecklistItem { Id = "c", Rect = new ItemRect(0, 0, Width, 300), Title = "Hoje", Entries = [new("a", "Uma", false)], Style = Style };

        var onTitle = StyledItemPainter.HotspotAt(list, Width, new Point(Width / 2.0, Style.Size));

        Assert.Null(onTitle);
    }

    [Fact]
    public void A_counter_has_minus_then_plus_on_its_right()
    {
        var counter = new CounterItem { Id = "n", Rect = new ItemRect(0, 0, Width, 200), Label = "Dias", Style = Style };

        var hotspots = StyledItemPainter.Hotspots(counter, Width);
        var minus = hotspots.Single(h => h.Action == HotspotAction.Decrement);
        var plus = hotspots.Single(h => h.Action == HotspotAction.Increment);

        Assert.True(minus.Area.Right <= plus.Area.Left + 1, "Minus comes before plus.");
        Assert.True(minus.Area.Left > Width / 2.0, "Both sit on the right, away from the number.");
    }

    [Fact]
    public void The_number_of_a_counter_is_not_a_button()
    {
        var counter = new CounterItem { Id = "n", Rect = new ItemRect(0, 0, Width, 200), Label = "Dias", Style = Style };
        var height = StyledItemPainter.RequiredHeight(counter);

        Assert.Null(StyledItemPainter.HotspotAt(counter, Width, new Point(Style.Size * 1.5, height - (Style.Size * 2))));
    }

    [Fact]
    public void A_click_along_the_bar_gives_the_percentage_under_it()
    {
        var progress = new ProgressItem { Id = "p", Rect = new ItemRect(0, 0, Width, 120), Label = "Leitura", Style = Style };

        var bar = StyledItemPainter.Hotspots(progress, Width).Single(h => h.Action == HotspotAction.SetProgress);

        Assert.Equal(0, bar.PercentAt(bar.Area.Left - 50));
        Assert.Equal(25, bar.PercentAt(bar.Area.Left + (bar.Area.Width / 4)));
        Assert.Equal(100, bar.PercentAt(bar.Area.Right + 50));
    }

    [Fact]
    public void The_whole_row_of_a_switch_flips_it()
    {
        var toggle = new ToggleItem { Id = "s", Rect = new ItemRect(0, 0, Width, 120), Label = "Modo", Style = Style };

        var row = StyledItemPainter.Hotspots(toggle, Width).Single();

        Assert.Equal(HotspotAction.Toggle, row.Action);
        Assert.Equal(0, row.Area.Left);
        Assert.Equal(Width, row.Area.Width);
    }

    [Fact]
    public void The_buttons_of_a_counter_are_drawn_only_in_the_editor()
    {
        var counter = new CounterItem { Id = "n", Rect = new ItemRect(0, 0, Width, 200), Value = 5, Style = Style };
        var height = StyledItemPainter.RequiredHeight(counter);
        var plus = StyledItemPainter.Hotspots(counter, Width).Single(h => h.Action == HotspotAction.Increment).Area;

        var published = Bright(Draw(counter, height, interactive: false), plus);
        var editor = Bright(Draw(counter, height, interactive: true), plus);

        Assert.Equal(0, published);
        Assert.True(editor > 0, "The editor should show the + button.");
    }

    [Fact]
    public void The_editor_and_the_wallpaper_need_the_same_room()
    {
        // The buttons only draw into space that is there either way, so nothing moves when publishing.
        var counter = new CounterItem { Id = "n", Rect = new ItemRect(0, 0, Width, 200), Label = "Dias", Style = Style };

        var everything = StyledItemPainter.Hotspots(counter, Width).Max(h => h.Area.Bottom);

        Assert.True(everything <= StyledItemPainter.RequiredHeight(counter) + Style.Size);
    }

    [Fact]
    public void A_fuller_bar_draws_more()
    {
        var empty = new ProgressItem { Id = "p", Rect = new ItemRect(0, 0, Width, 120), Percent = 10, Style = Style };
        var full = empty with { Percent = 90 };
        var height = StyledItemPainter.RequiredHeight(empty);
        var area = new Rect(0, 0, Width, height);

        Assert.True(Bright(Draw(full, height, interactive: false), area) > Bright(Draw(empty, height, interactive: false), area));
    }

    [Fact]
    public void Each_state_of_a_switch_looks_different()
    {
        var off = new ToggleItem { Id = "s", Rect = new ItemRect(0, 0, Width, 120), Label = "Modo", Style = Style };
        var on = off with { On = true };
        var height = StyledItemPainter.RequiredHeight(off);

        Assert.NotEqual(Pixels(Draw(off, height, interactive: false)), Pixels(Draw(on, height, interactive: false)));
    }

    [Fact]
    public void A_label_needs_room_of_its_own()
    {
        var bare = new ToggleItem { Id = "s", Rect = new ItemRect(0, 0, Width, 120), Style = Style };
        var labeled = bare with { Label = "Modo" };

        Assert.True(StyledItemPainter.RequiredHeight(labeled) > StyledItemPainter.RequiredHeight(bare));
    }

    private static BitmapSource Draw(StyledItem item, double height, bool interactive)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, Width, height));
            StyledItemPainter.Draw(context, item, Width, height, interactive);
        }

        var bitmap = new RenderTargetBitmap(Width, (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    /// <summary>Pixels that are not black, inside an area.</summary>
    private static int Bright(BitmapSource image, Rect area)
    {
        var pixels = Pixels(image);
        var count = 0;
        for (var y = Math.Max(0, (int)area.Top); y < Math.Min(image.PixelHeight, (int)area.Bottom); y++)
        {
            for (var x = Math.Max(0, (int)area.Left); x < Math.Min(image.PixelWidth, (int)area.Right); x++)
            {
                if (pixels[(((y * image.PixelWidth) + x) * 4) + 2] > 60)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
