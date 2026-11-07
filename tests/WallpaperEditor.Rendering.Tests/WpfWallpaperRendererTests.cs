using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Rendering.Tests;

public sealed class WpfWallpaperRendererTests : IDisposable
{
    private static readonly Color Red = Color.FromRgb(220, 20, 20);
    private static readonly Color Blue = Color.FromRgb(20, 20, 220);
    private static readonly Color Black = Color.FromRgb(0, 0, 0);

    private readonly string _folder = Directory.CreateTempSubdirectory("we-render-").FullName;
    private readonly WpfWallpaperRenderer _renderer = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(FitMode.Fill)]
    [InlineData(FitMode.Fit)]
    [InlineData(FitMode.Center)]
    [InlineData(FitMode.Stretch)]
    public async Task The_output_always_has_the_exact_requested_size(FitMode mode)
    {
        var output = await Render(WritePng(SplitHorizontally(600, 300)), mode, new PixelSize(1080, 1920));

        Assert.Equal(1080, output.PixelWidth);
        Assert.Equal(1920, output.PixelHeight);
    }

    [Fact]
    public async Task The_output_is_an_opaque_png()
    {
        var path = Path.Combine(_folder, "out.png");

        await _renderer.RenderToFileAsync(new StaticImageRequest(WritePng(SplitHorizontally(100, 100)), FitMode.Fill, new PixelSize(50, 50)), path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes[..4]);

        using var file = File.OpenRead(path);
        var frame = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(PixelFormats.Bgr24, frame.Format);
    }

    [Fact]
    public async Task Fill_crops_the_sides_of_a_wide_image_on_a_vertical_monitor()
    {
        // Left half red, right half blue: filling a narrow target keeps only the middle of the image.
        var output = await Render(WritePng(SplitHorizontally(400, 100)), FitMode.Fill, new PixelSize(50, 100));

        AssertColor(Red, Pixel(output, 5, 50));
        AssertColor(Blue, Pixel(output, 45, 50));
    }

    [Fact]
    public async Task Fit_leaves_black_bars_where_the_image_does_not_reach()
    {
        var output = await Render(WritePng(SplitHorizontally(100, 50)), FitMode.Fit, new PixelSize(100, 200));

        AssertColor(Black, Pixel(output, 50, 10));
        AssertColor(Black, Pixel(output, 50, 190));
        AssertColor(Red, Pixel(output, 10, 100));
        AssertColor(Blue, Pixel(output, 90, 100));
    }

    [Fact]
    public async Task Center_does_not_scale_the_image()
    {
        var output = await Render(WritePng(SplitHorizontally(20, 20)), FitMode.Center, new PixelSize(100, 100));

        AssertColor(Black, Pixel(output, 35, 50));
        AssertColor(Red, Pixel(output, 42, 50));
        AssertColor(Blue, Pixel(output, 58, 50));
        AssertColor(Black, Pixel(output, 65, 50));
    }

    [Fact]
    public async Task An_image_already_at_the_target_size_is_kept_pixel_for_pixel()
    {
        // A 1-pixel checkerboard turns to flat gray under any resampling.
        var source = Checkerboard(64, 64);

        var output = await Render(WritePng(source), FitMode.Fill, new PixelSize(64, 64));

        Assert.Equal(Pixels(source), Pixels(output));
    }

    [Fact]
    public async Task Centering_a_smaller_image_keeps_it_pixel_for_pixel()
    {
        // The size difference is odd on purpose: half a pixel off would blur every pixel.
        var source = Checkerboard(33, 33);

        var output = await Render(WritePng(source), FitMode.Center, new PixelSize(64, 64));

        var crop = new CroppedBitmap(output, new System.Windows.Int32Rect(15, 15, 33, 33));
        crop.Freeze();
        Assert.Equal(Pixels(source), Pixels(crop));
    }

    [Fact]
    public async Task Transparent_areas_become_black()
    {
        var pixels = new byte[10 * 10 * 4];
        var transparent = BitmapSource.Create(10, 10, 96, 96, PixelFormats.Bgra32, null, pixels, 40);

        var output = await Render(WritePng(transparent), FitMode.Fill, new PixelSize(10, 10));

        AssertColor(Black, Pixel(output, 5, 5));
    }

    [Theory]
    [InlineData((ushort)6, "red-on-top")]
    [InlineData((ushort)8, "blue-on-top")]
    public async Task A_photo_turned_by_exif_is_shown_upright(ushort orientation, string expected)
    {
        // Stored as 200x100 with red on the left. Turning it a quarter gives a 100x200 image.
        var path = WriteJpeg(SplitHorizontally(200, 100), orientation);

        var output = await Render(path, FitMode.Stretch, new PixelSize(100, 200));

        var (top, bottom) = expected == "red-on-top" ? (Red, Blue) : (Blue, Red);
        AssertColor(top, Pixel(output, 50, 20), tolerance: 40);
        AssertColor(bottom, Pixel(output, 50, 180), tolerance: 40);
    }

    [Fact]
    public async Task The_fit_uses_the_orientation_as_a_person_sees_it()
    {
        // Upright, this photo is 100x200: the same shape as the target, so nothing is left black.
        var path = WriteJpeg(SplitHorizontally(200, 100), orientation: 6);

        var output = await Render(path, FitMode.Fit, new PixelSize(100, 200));

        AssertNotBlack(Pixel(output, 5, 100));
        AssertNotBlack(Pixel(output, 95, 100));
    }

    [Fact]
    public async Task A_large_image_is_reduced_without_failing()
    {
        var output = await Render(WritePng(SplitHorizontally(3000, 1500)), FitMode.Fill, new PixelSize(300, 150));

        AssertColor(Red, Pixel(output, 20, 75));
        AssertColor(Blue, Pixel(output, 280, 75));
    }

    [Fact]
    public async Task A_missing_file_is_reported_as_an_image_problem()
    {
        var missing = Path.Combine(_folder, "missing.png");

        await Assert.ThrowsAsync<ImageLoadException>(() => Render(missing, FitMode.Fill, new PixelSize(10, 10)));
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_reported_as_an_image_problem()
    {
        var path = Path.Combine(_folder, "notes.png");
        File.WriteAllText(path, "this is not an image");

        await Assert.ThrowsAsync<ImageLoadException>(() => Render(path, FitMode.Fill, new PixelSize(10, 10)));
    }

    [Fact]
    public async Task A_path_with_special_characters_works()
    {
        var path = Path.Combine(_folder, "foto #1 & ção.png");
        File.Copy(WritePng(SplitHorizontally(10, 10)), path);

        var output = await Render(path, FitMode.Fill, new PixelSize(10, 10));

        Assert.Equal(10, output.PixelWidth);
    }

    [Fact]
    public async Task A_cancelled_render_writes_nothing()
    {
        var path = Path.Combine(_folder, "out.png");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _renderer.RenderToFileAsync(
            new StaticImageRequest(WritePng(SplitHorizontally(10, 10)), FitMode.Fill, new PixelSize(10, 10)),
            path,
            new CancellationToken(canceled: true)));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task The_preview_matches_the_final_drawing()
    {
        var request = new StaticImageRequest(WritePng(SplitHorizontally(100, 100)), FitMode.Fill, new PixelSize(40, 40));

        var preview = await _renderer.RenderPreviewAsync(request);

        Assert.True(preview.IsFrozen);
        AssertColor(Red, Pixel(preview, 5, 20));
        AssertColor(Blue, Pixel(preview, 35, 20));
    }

    private async Task<BitmapSource> Render(string source, FitMode mode, PixelSize target)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.png");
        await _renderer.RenderToFileAsync(new StaticImageRequest(source, mode, target), path);

        using var file = File.OpenRead(path);
        var frame = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];

        // An independent frozen copy: the caller may resume on another thread after awaiting this, and a
        // frame stays tied to the thread that created its decoder even when frozen.
        var copy = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Bgr24, null, Pixels(frame), frame.PixelWidth * 3);
        copy.Freeze();
        return copy;
    }

    private string WritePng(BitmapSource image)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private string WriteJpeg(BitmapSource image, ushort orientation)
    {
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);

        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.jpg");
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private static BitmapSource SplitHorizontally(int width, int height) =>
        Generate(width, height, (x, _) => x < width / 2 ? Red : Blue);

    private static BitmapSource Checkerboard(int width, int height) =>
        Generate(width, height, (x, y) => (x + y) % 2 == 0 ? Colors.White : Colors.Black);

    private static BitmapSource Generate(int width, int height, Func<int, int, Color> color)
    {
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = color(x, y);
                var o = ((y * width) + x) * 3;
                pixels[o] = c.B;
                pixels[o + 1] = c.G;
                pixels[o + 2] = c.R;
            }
        }

        // Frozen so a test can still read it after an await resumes on another thread.
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, width * 3);
        image.Freeze();
        return image;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        var stride = image.PixelWidth * 3;
        var pixels = new byte[stride * image.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
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

    private static void AssertNotBlack(Color actual) =>
        Assert.True(actual.R + actual.G + actual.B > 60, $"Expected image content, got {actual}.");
}
