using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Rendering.Tests;

public sealed class TextRenderingTests : IDisposable
{
    private static readonly PixelSize Canvas = new(400, 200);
    private static readonly Color Blue = Color.FromRgb(20, 20, 220);

    private readonly string _folder = Directory.CreateTempSubdirectory("we-text-").FullName;
    private readonly WpfWallpaperRenderer _renderer = new();
    private readonly AssetRef _background;

    public TextRenderingTests()
    {
        var pixels = new byte[400 * 200 * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = Blue.B;
            pixels[i + 1] = Blue.G;
            pixels[i + 2] = Blue.R;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(400, 200, 96, 96, PixelFormats.Bgr24, null, pixels, 1200)));
        using var file = File.Create(Path.Combine(_folder, "bg.png"));
        encoder.Save(file);
        _background = new AssetRef("bg.png", "bg.png");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Text_appears_in_the_image()
    {
        var output = await Render(Text("MMMM", panel: "#00000000"));

        Assert.True(CountBright(output) > 0, "White text should appear over the blue background.");
    }

    [Fact]
    public async Task Different_text_makes_a_different_image()
    {
        var one = Pixels(await Render(Text("Um", panel: "#00000000")));
        var other = Pixels(await Render(Text("Outro", panel: "#00000000")));

        Assert.NotEqual(one, other);
    }

    [Fact]
    public async Task Transparent_text_leaves_nothing_on_a_transparent_panel()
    {
        var item = Text("MMMM", panel: "#00000000");
        var invisible = item with { Style = item.Style.WithTextOpacity(0) };

        var output = await Render(invisible);

        Assert.Equal(0, CountBright(output));
    }

    [Fact]
    public async Task Text_is_clipped_to_its_rectangle()
    {
        var tall = new TextItem
        {
            Id = "t",
            Rect = new ItemRect(10, 10, 380, 60),
            Text = string.Join('\n', Enumerable.Repeat("MMMMMMMMMMMMMMMMMMMM", 20)),
            Style = TextStyle.Default with { Panel = "#00000000" },
        };

        var output = await Render(tall);

        // Well below the rectangle, nothing but the background.
        for (var x = 10; x < 390; x += 3)
        {
            Assert.True(Pixel(output, x, 150) == Blue, $"Text spilled below its rectangle at x={x}.");
        }
    }

    [Fact]
    public async Task A_rotated_text_turns_with_its_panel()
    {
        var straight = Text("MMMM", panel: "#FF00C800");
        var turned = straight with { Rect = new ItemRect(100, 90, 200, 20), Rotation = 90 };

        var output = await Render(turned);

        // Turned a quarter around (200,100), the bar now runs vertically through the center.
        Assert.True(Pixel(output, 200, 30).G > 150, "The panel should reach upward.");
        Assert.True(Pixel(output, 120, 100) == Blue, "The panel should no longer extend sideways.");
    }

    [Fact]
    public void More_lines_need_more_room()
    {
        var one = Text("Uma linha", panel: "#00000000");
        var three = one with { Text = "Uma\nDuas\nTrês" };

        Assert.True(StyledItemPainter.RequiredHeight(three) > StyledItemPainter.RequiredHeight(one));
    }

    [Fact]
    public void Long_text_wraps_and_needs_more_room_on_a_narrow_item()
    {
        var long_ = string.Join(' ', Enumerable.Repeat("palavra", 30));
        var wide = Text(long_, panel: "#00000000") with { Rect = new ItemRect(0, 0, 2000, 50) };
        var narrow = wide with { Rect = new ItemRect(0, 0, 200, 50) };

        Assert.True(StyledItemPainter.RequiredHeight(narrow) > StyledItemPainter.RequiredHeight(wide));
    }

    [Fact]
    public void A_larger_font_needs_more_room()
    {
        var small = Text("Olá", panel: "#00000000");
        var large = small with { Style = small.Style with { Size = 60 } };

        Assert.True(StyledItemPainter.RequiredHeight(large) > StyledItemPainter.RequiredHeight(small));
    }

    private static TextItem Text(string text, string panel) => new()
    {
        Id = "t",
        Rect = new ItemRect(20, 20, 360, 120),
        Text = text,
        Style = TextStyle.Default with { Panel = panel },
    };

    private async Task<BitmapSource> Render(DocumentItem item)
    {
        var document = new WallpaperDocument(new BackgroundSpec(_background, FitMode.Fill), [item]);
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.png");
        await _renderer.RenderDocumentToFileAsync(new DocumentRenderRequest(document, Canvas, name => Path.Combine(_folder, name), Canvas), path);

        using var file = File.OpenRead(path);
        var frame = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var copy = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, frame.Format, null, Raw(frame), frame.PixelWidth * 3);
        copy.Freeze();
        return copy;
    }

    private static byte[] Raw(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 3];
        image.CopyPixels(pixels, image.PixelWidth * 3, 0);
        return pixels;
    }

    private static byte[] Pixels(BitmapSource image) => Raw(new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0));

    /// <summary>Pixels with a lot of red, which the blue background has none of.</summary>
    private static int CountBright(BitmapSource image)
    {
        var pixels = Pixels(image);
        var count = 0;
        for (var i = 2; i < pixels.Length; i += 3)
        {
            if (pixels[i] > 120)
            {
                count++;
            }
        }

        return count;
    }

    private static Color Pixel(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        var pixel = new byte[3];
        converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 3, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }
}
