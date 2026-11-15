using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Rendering.Tests;

public sealed class DocumentRenderingTests : IDisposable
{
    private static readonly Color Red = Color.FromRgb(220, 20, 20);
    private static readonly Color Green = Color.FromRgb(20, 200, 20);
    private static readonly Color Blue = Color.FromRgb(20, 20, 220);
    private static readonly Color Black = Color.FromRgb(0, 0, 0);
    private static readonly PixelSize Canvas = new(400, 200);

    private readonly string _folder = Directory.CreateTempSubdirectory("we-doc-").FullName;
    private readonly WpfWallpaperRenderer _renderer = new();
    private readonly AssetRef _blue;
    private readonly AssetRef _red;
    private readonly AssetRef _green;

    public DocumentRenderingTests()
    {
        _blue = WriteAsset("blue.png", Blue, 400, 200);
        _red = WriteAsset("red.png", Red, 100, 100);
        _green = WriteAsset("green.png", Green, 100, 100);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task The_result_is_one_flat_image_of_the_exact_size()
    {
        var output = await Render(Document(Image(_red, 10, 10, 50, 50)), Canvas);

        Assert.Equal(400, output.PixelWidth);
        Assert.Equal(200, output.PixelHeight);
        Assert.Equal(PixelFormats.Bgr24, output.Format);
    }

    [Fact]
    public async Task Items_land_inside_the_background_at_their_position()
    {
        var output = await Render(Document(Image(_red, 100, 50, 80, 80)), Canvas);

        AssertColor(Blue, Pixel(output, 50, 50));
        AssertColor(Red, Pixel(output, 140, 90));
        AssertColor(Blue, Pixel(output, 300, 90));
    }

    [Fact]
    public async Task Later_items_are_drawn_on_top_of_earlier_ones()
    {
        var output = await Render(Document(Image(_red, 100, 50, 80, 80), Image(_green, 100, 50, 80, 80)), Canvas);

        AssertColor(Green, Pixel(output, 140, 90));
    }

    [Fact]
    public async Task A_mirrored_image_is_flipped_left_to_right_in_place()
    {
        var halves = WriteHalves("halves.png");
        var plain = Image(halves, 100, 50, 200, 100);

        var straight = await Render(Document(plain), Canvas);
        var mirrored = await Render(Document(plain with { Mirrored = true }), Canvas);

        AssertColor(Red, Pixel(straight, 130, 100));
        AssertColor(Green, Pixel(mirrored, 130, 100));
        AssertColor(Red, Pixel(mirrored, 270, 100));
        AssertColor(Blue, Pixel(mirrored, 60, 100));
        AssertColor(Blue, Pixel(mirrored, 340, 100));
    }

    [Fact]
    public async Task A_mirrored_image_still_turns_around_its_center()
    {
        // Flipped, then turned half around: left and right come back where they were.
        var halves = WriteHalves("halves-turned.png");
        var item = Image(halves, 100, 50, 200, 100) with { Mirrored = true, Rotation = 180 };

        var output = await Render(Document(item), Canvas);

        AssertColor(Red, Pixel(output, 130, 100));
        AssertColor(Green, Pixel(output, 270, 100));
    }

    /// <summary>Red on the left half, green on the right: shows which way an image faces.</summary>
    private AssetRef WriteHalves(string name)
    {
        const int Width = 100, Height = 50;
        var pixels = new byte[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var color = x < Width / 2 ? Red : Green;
                var i = ((y * Width) + x) * 3;
                (pixels[i], pixels[i + 1], pixels[i + 2]) = (color.B, color.G, color.R);
            }
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgr24, null, pixels, Width * 3)));
        using (var file = File.Create(Path.Combine(_folder, name)))
        {
            encoder.Save(file);
        }

        return new AssetRef(name, name);
    }

    [Fact]
    public async Task Rotation_turns_an_item_around_its_center()
    {
        // A wide bar turned a quarter becomes a tall one, through the same center.
        var bar = Image(_red, 150, 90, 100, 20) with { Rotation = 90 };

        var output = await Render(Document(bar), Canvas);

        AssertColor(Red, Pixel(output, 200, 60));
        AssertColor(Red, Pixel(output, 200, 140));
        AssertColor(Blue, Pixel(output, 160, 100));
        AssertColor(Blue, Pixel(output, 240, 100));
    }

    [Fact]
    public async Task Opacity_lets_what_is_underneath_show_through()
    {
        var half = Image(_red, 100, 50, 80, 80) with { Opacity = 0.5 };

        var output = await Render(Document(half), Canvas);

        var pixel = Pixel(output, 140, 90);
        Assert.InRange(pixel.R, 100, 140);
        Assert.InRange(pixel.B, 100, 140);
    }

    [Fact]
    public async Task A_canvas_of_another_shape_adapts_as_a_whole()
    {
        // Filling a taller target crops the canvas at the sides: its center stays the center of the result.
        var output = await Render(Document(Image(_red, 150, 50, 100, 100)), new PixelSize(100, 200));

        AssertColor(Red, Pixel(output, 50, 100));
        Assert.Equal(100, output.PixelWidth);
        Assert.Equal(200, output.PixelHeight);
    }

    [Fact]
    public async Task A_smaller_target_scales_the_items_with_the_canvas()
    {
        var output = await Render(Document(Image(_red, 200, 0, 200, 200)), new PixelSize(200, 100));

        AssertColor(Blue, Pixel(output, 50, 50));
        AssertColor(Red, Pixel(output, 150, 50));
    }

    [Fact]
    public async Task A_checklist_draws_its_panel_and_is_clipped_to_its_rectangle()
    {
        var list = new ChecklistItem
        {
            Id = "c",
            Rect = new ItemRect(20, 20, 200, 60),
            Title = "Hoje",
            Entries = [new("1", "Uma", false), new("2", "Duas", false), new("3", "Três", false), new("4", "Quatro", false)],
            Style = TextStyle.Default with { Panel = "#FF00C800" },
        };

        var output = await Render(Document(list), Canvas);

        AssertColor(Green, Pixel(output, 215, 40), tolerance: 40);

        // Below the rectangle, the entries that do not fit must not spill over the background.
        for (var x = 20; x < 220; x += 10)
        {
            AssertColor(Blue, Pixel(output, x, 90));
        }
    }

    [Fact]
    public async Task Marking_an_entry_changes_the_picture()
    {
        var pending = Checklist(done: false);
        var done = Checklist(done: true);

        var before = Pixels(await Render(Document(pending), Canvas));
        var after = Pixels(await Render(Document(done), Canvas));

        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task The_font_changes_the_picture()
    {
        var plain = Checklist(done: false);
        var mono = plain with { Style = plain.Style with { Font = "Consolas" } };

        var before = Pixels(await Render(Document(plain), Canvas));
        var after = Pixels(await Render(Document(mono), Canvas));

        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task A_font_that_is_not_installed_still_draws_the_text()
    {
        var list = Checklist(done: false);
        var missing = list with { Style = list.Style with { Font = "Fonte Que Nao Existe 123", Panel = "#00000000" } };

        var output = await Render(Document(missing), Canvas);

        // White text over the blue background: some pixels of the panel area must have changed.
        Assert.Contains(Enumerable.Range(10, 280), x => Enumerable.Range(10, 100).Any(y => Pixel(output, x, y).R > 150));
    }

    [Fact]
    public async Task A_transparent_panel_shows_the_background_behind_the_text()
    {
        var list = Checklist(done: false) with { Rect = new ItemRect(10, 10, 300, 150) };
        var clear = list with { Style = list.Style.WithPanelOpacity(0) };

        var output = await Render(Document(clear), Canvas);

        // A corner of the item, away from any text, is the background itself.
        AssertColor(Blue, Pixel(output, 305, 155));
    }

    [Fact]
    public async Task An_opaque_panel_hides_the_background()
    {
        var list = Checklist(done: false);
        var solid = list with { Style = list.Style.WithPanelOpacity(1) };

        var output = await Render(Document(solid), Canvas);

        AssertColor(Color.FromRgb(0x10, 0x12, 0x18), Pixel(output, 305, 150));
    }

    [Fact]
    public async Task Fully_transparent_text_leaves_only_the_panel()
    {
        var list = Checklist(done: true);
        var noText = list with { Style = list.Style.WithTextOpacity(0) };
        var noEntries = list with { Title = string.Empty, Entries = [] };

        var without = Pixels(await Render(Document(noText), Canvas));
        var empty = Pixels(await Render(Document(noEntries), Canvas));

        Assert.Equal(empty, without);
    }

    [Fact]
    public async Task Half_transparent_text_is_dimmer_than_opaque_text()
    {
        var list = Checklist(done: false) with { Style = TextStyle.Default with { Panel = "#FF000000" } };
        var half = list with { Style = list.Style.WithTextOpacity(0.5) };

        var bright = (await Render(Document(list), Canvas));
        var dim = (await Render(Document(half), Canvas));

        Assert.True(Brightest(dim) < Brightest(bright));
    }

    [Fact]
    public void The_checklist_needs_more_room_as_entries_are_added()
    {
        var one = Checklist(done: false);
        var three = one with { Entries = [.. one.Entries, new("b", "Mais uma", false), new("c", "E outra", false)] };

        Assert.True(StyledItemPainter.RequiredHeight(three) > StyledItemPainter.RequiredHeight(one));
    }

    [Fact]
    public void A_long_entry_wraps_and_needs_more_room_on_a_narrow_list()
    {
        var entry = new ChecklistEntry("a", string.Join(' ', Enumerable.Repeat("palavra", 20)), false);
        var wide = Checklist(done: false) with { Entries = [entry], Rect = new ItemRect(0, 0, 1000, 100) };
        var narrow = wide with { Rect = new ItemRect(0, 0, 200, 100) };

        Assert.True(StyledItemPainter.RequiredHeight(narrow) > StyledItemPainter.RequiredHeight(wide));
    }

    [Fact]
    public async Task An_unreadable_asset_fails_the_publish_by_name()
    {
        var missing = new AssetRef("gone.png", "foto de casa.png");
        var path = Path.Combine(_folder, "out.png");

        var error = await Assert.ThrowsAsync<ImageLoadException>(() => _renderer.RenderDocumentToFileAsync(Request(Document(Image(missing, 0, 0, 50, 50)), Canvas), path));

        Assert.Equal("foto de casa.png", error.ImagePath);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task An_unreadable_asset_can_be_left_out_on_request()
    {
        var missing = new AssetRef("gone.png", "foto.png");
        var path = Path.Combine(_folder, "out.png");
        var request = Request(Document(Image(missing, 0, 0, 50, 50), Image(_red, 100, 50, 80, 80)), Canvas) with { SkipUnreadableAssets = true };

        await _renderer.RenderDocumentToFileAsync(request, path);

        var output = Read(path);
        AssertColor(Blue, Pixel(output, 25, 25));
        AssertColor(Red, Pixel(output, 140, 90));
    }

    [Fact]
    public async Task A_missing_background_leaves_black_rather_than_failing_the_preview()
    {
        var document = new WallpaperDocument(new BackgroundSpec(new AssetRef("gone.png", "x.png"), FitMode.Fill), []);

        var preview = await _renderer.RenderDocumentPreviewAsync(Request(document, Canvas));

        AssertColor(Black, Pixel(preview, 200, 100));
    }

    [Fact]
    public async Task The_preview_matches_the_final_image()
    {
        var document = Document(Image(_red, 100, 50, 80, 80), Checklist(done: true));
        var path = Path.Combine(_folder, "out.png");

        await _renderer.RenderDocumentToFileAsync(Request(document, Canvas), path);
        var preview = await _renderer.RenderDocumentPreviewAsync(Request(document, Canvas));

        Assert.Equal(Pixels(Read(path)), Pixels(preview));
    }

    /// <summary>The brightest red value inside the checklist's area.</summary>
    private static int Brightest(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        var row = new byte[300 * 150 * 3];
        converted.CopyPixels(new System.Windows.Int32Rect(10, 10, 300, 150), row, 300 * 3, 0);
        return Enumerable.Range(0, row.Length / 3).Max(i => row[(i * 3) + 2]);
    }

    private static ChecklistItem Checklist(bool done) => new()
    {
        Id = "c",
        Rect = new ItemRect(10, 10, 300, 150),
        Title = "Hoje",
        Entries = [new("a", "Comprar café", done)],
    };

    private WallpaperDocument Document(params DocumentItem[] items) =>
        new(new BackgroundSpec(_blue, FitMode.Fill), items);

    private static ImageItem Image(AssetRef asset, double x, double y, double width, double height) =>
        new() { Id = Guid.NewGuid().ToString("N"), Rect = new ItemRect(x, y, width, height), Source = asset };

    private DocumentRenderRequest Request(WallpaperDocument document, PixelSize target) =>
        new(document, Canvas, name => Path.Combine(_folder, name), target);

    private async Task<BitmapSource> Render(WallpaperDocument document, PixelSize target)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.png");
        await _renderer.RenderDocumentToFileAsync(Request(document, target), path);
        return Read(path);
    }

    private static BitmapSource Read(string path)
    {
        using var file = File.OpenRead(path);
        var frame = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var copy = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, frame.Format, null, RawPixels(frame), frame.PixelWidth * 3);
        copy.Freeze();
        return copy;
    }

    private AssetRef WriteAsset(string name, Color color, int width, int height)
    {
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
        }

        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(_folder, name));
        encoder.Save(file);
        return new AssetRef(name, name);
    }

    private static byte[] RawPixels(BitmapSource image)
    {
        var stride = image.PixelWidth * 3;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        return RawPixels(converted);
    }

    private static Color Pixel(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        var pixel = new byte[3];
        converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 3, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    private static void AssertColor(Color expected, Color actual, int tolerance = 12)
    {
        var close = Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance;

        Assert.True(close, $"Expected about {expected}, got {actual}.");
    }
}
