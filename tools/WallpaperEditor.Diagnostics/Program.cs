// Validates the undocumented or unverified Windows behaviour the app relies on, and measures what
// changing a wallpaper costs. See docs/06-roadmap.md for the tests this serves.
//
// Every command that writes targets only the portrait monitor, found by shape rather than by a stored
// id, and stops if there is not exactly one. Run `snapshot` first so that `restore` can undo the rest.
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Platform.Windows;

namespace WallpaperEditor.Diagnostics;

internal static class Program
{
    private static readonly DesktopWallpaperService Service = new();

    [STAThread]
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            switch (args.FirstOrDefault())
            {
                case "monitors": Monitors(); return 0;
                case "snapshot": Snapshot(args[1]); return 0;
                case "restore": Restore(args[1]); return 0;
                case "images": Images(args[1]); return 0;
                case "apply": Apply(args[1]); return 0;
                case "rewrite-same-path": RewriteSamePath(args[1], args[2]); return 0;
                case "bench": Bench(args[1], int.Parse(args[2]), int.Parse(args[3])); return 0;
                case "compare": Compare(args[1], args[2]); return 0;
                default:
                    Console.Error.WriteLine("""
                        monitors                        list what the OS enumerates (read-only)
                        snapshot <file>                 save the current wallpaper of each monitor (read-only)
                        restore <file>                  put the saved wallpaper back on the portrait monitor
                        images <dir>                    write test images sized for the portrait monitor
                        apply <image>                   set an image on the portrait monitor
                        rewrite-same-path <src> <dst>   overwrite dst with src, then set dst again
                        bench <dir> <count> <gapMs>     time repeated changes, as the app would do them
                        compare <a> <b>                 compare two images pixel by pixel
                        """);
                    return 2;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or IndexOutOfRangeException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static void Monitors()
    {
        var names = DisplayNames.Read();
        var candidates = Service.GetCandidates();

        Console.WriteLine($"enumerated: {candidates.Count}");
        foreach (var c in candidates)
        {
            var named = names.ContainsKey(c.Id.Value);
            Console.WriteLine($"  {c.Id}");
            Console.WriteLine($"    bounds={c.Bounds?.ToString() ?? "none"} displayConfigMatch={named}");
        }

        Console.WriteLine($"displayConfig paths: {names.Count}");
        foreach (var (path, name) in names)
        {
            Console.WriteLine($"  {name} <- {path}");
        }

        Console.WriteLine("usable:");
        foreach (var m in Service.GetMonitors())
        {
            Console.WriteLine($"  {m.Name} {m.Bounds} primary={m.IsPrimary} portrait={m.IsPortrait}");
        }

        Console.WriteLine($"position={Service.GetPosition()} slideshow={Service.GetSlideshowState()}");
    }

    private static void Snapshot(string file)
    {
        var state = Service.GetMonitors().ToDictionary(m => m.Id.Value, m => Service.GetWallpaper(m.Id));
        File.WriteAllText(file, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"saved {state.Count} monitors");
    }

    private static void Restore(string file)
    {
        var state = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(file))
            ?? throw new InvalidOperationException("Empty snapshot.");
        var portrait = Portrait();
        var original = state[portrait.Id.Value] ?? throw new InvalidOperationException("No saved wallpaper.");
        Service.SetWallpaper(portrait.Id, original);
        Console.WriteLine($"restored {portrait.Name}: {Service.GetWallpaper(portrait.Id)}");
        VerifyOthers(state, portrait);
    }

    private static void Images(string dir)
    {
        Directory.CreateDirectory(dir);
        var size = Portrait().Bounds;
        WritePng(Pattern(size.Width, size.Height, "TESTE A", Color.FromRgb(20, 50, 90)), Path.Combine(dir, "pattern-a.png"));
        WritePng(Pattern(size.Width, size.Height, "TESTE B", Color.FromRgb(90, 30, 40)), Path.Combine(dir, "pattern-b.png"));
        WritePng(Photo(size.Width, size.Height, 1), Path.Combine(dir, "photo-a.png"));
        WritePng(Photo(size.Width, size.Height, 2), Path.Combine(dir, "photo-b.png"));
        foreach (var f in Directory.GetFiles(dir, "*.png"))
        {
            Console.WriteLine($"{Path.GetFileName(f)} {new FileInfo(f).Length / 1048576.0:F2} MiB");
        }
    }

    private static void Apply(string image)
    {
        var before = Others();
        var portrait = Portrait();
        var sw = Stopwatch.StartNew();
        Service.SetWallpaper(portrait.Id, image);
        Console.WriteLine($"{portrait.Name}: SetWallpaper {sw.Elapsed.TotalMilliseconds:F0} ms");
        Console.WriteLine($"reported: {Service.GetWallpaper(portrait.Id)}");
        VerifyOthers(before, portrait);
    }

    private static void RewriteSamePath(string source, string destination)
    {
        File.Copy(source, destination, overwrite: true);
        Apply(destination);
    }

    private static void Bench(string dir, int count, int gapMs)
    {
        var portrait = Portrait();
        var before = Others();
        var images = new[] { Path.Combine(dir, "photo-a.png"), Path.Combine(dir, "photo-b.png") };
        var published = Directory.CreateDirectory(Path.Combine(dir, "published")).FullName;
        var set = new List<double>();
        var copy = new List<double>();
        var processed = new List<double>();
        string? previous = null;
        var mismatches = 0;

        // SetWallpaper only schedules the change and returns within milliseconds. The shell's last
        // visible step is rewriting this file, which is the closest thing to "done" that can be observed.
        var marker = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Themes\TranscodedWallpaper");

        for (var i = 0; i < count; i++)
        {
            // The same steps the app's publish pipeline will take: new name, set, delete the old file.
            var sw = Stopwatch.StartNew();
            var path = Path.Combine(published, $"{Guid.NewGuid():N}.png");
            File.Copy(images[i % 2], path);
            copy.Add(sw.Elapsed.TotalMilliseconds);

            var markerBefore = File.GetLastWriteTimeUtc(marker);
            sw.Restart();
            Service.SetWallpaper(portrait.Id, path);
            set.Add(sw.Elapsed.TotalMilliseconds);

            while (File.GetLastWriteTimeUtc(marker) == markerBefore && sw.ElapsedMilliseconds < 10_000)
            {
                Thread.Sleep(2);
            }

            processed.Add(sw.Elapsed.TotalMilliseconds);

            if (!string.Equals(Service.GetWallpaper(portrait.Id), path, StringComparison.OrdinalIgnoreCase))
            {
                mismatches++;
            }

            if (previous is not null)
            {
                File.Delete(previous);
            }

            previous = path;
            Thread.Sleep(gapMs);
        }

        Console.WriteLine($"swaps={count} gapMs={gapMs} monitor={portrait.Bounds.Width}x{portrait.Bounds.Height}");
        Console.WriteLine($"SetWallpaper first={set[0]:F0}ms median={Median(set):F0}ms p95={Percentile(set, 0.95):F0}ms max={set.Max():F0}ms");
        Console.WriteLine($"shell finished writing first={processed[0]:F0}ms median={Median(processed):F0}ms p95={Percentile(processed, 0.95):F0}ms max={processed.Max():F0}ms");
        Console.WriteLine($"copy median={Median(copy):F1}ms");
        Console.WriteLine($"reportedMismatches={mismatches}");
        VerifyOthers(before, portrait);
    }

    private static void Compare(string a, string b)
    {
        var left = Decode(a);
        var right = Decode(b);
        if (left.PixelWidth != right.PixelWidth || left.PixelHeight != right.PixelHeight)
        {
            Console.WriteLine($"size differs: {left.PixelWidth}x{left.PixelHeight} vs {right.PixelWidth}x{right.PixelHeight}");
            return;
        }

        var x = Pixels(left);
        var y = Pixels(right);
        long different = 0, maxDelta = 0, total = 0;
        for (var i = 0; i < x.Length; i += 3)
        {
            var delta = Math.Max(Math.Abs(x[i] - y[i]), Math.Max(Math.Abs(x[i + 1] - y[i + 1]), Math.Abs(x[i + 2] - y[i + 2])));
            if (delta > 0)
            {
                different++;
            }

            maxDelta = Math.Max(maxDelta, delta);
            total += delta;
        }

        var pixels = x.Length / 3;
        Console.WriteLine($"size={left.PixelWidth}x{left.PixelHeight} differentPixels={different}/{pixels} ({100.0 * different / pixels:F2}%) maxDelta={maxDelta} meanDelta={(double)total / pixels:F3}");
    }

    private static MonitorInfo Portrait()
    {
        var portraits = Service.GetMonitors().Where(m => m.IsPortrait).ToList();
        return portraits.Count == 1
            ? portraits[0]
            : throw new InvalidOperationException($"Expected exactly one portrait monitor, found {portraits.Count}.");
    }

    private static Dictionary<string, string?> Others()
    {
        var portrait = Portrait();
        return Service.GetMonitors()
            .Where(m => m.Id != portrait.Id)
            .ToDictionary(m => m.Id.Value, m => Service.GetWallpaper(m.Id));
    }

    private static void VerifyOthers(Dictionary<string, string?> before, MonitorInfo portrait)
    {
        var changed = Service.GetMonitors()
            .Where(m => m.Id != portrait.Id)
            .Where(m => !string.Equals(before.GetValueOrDefault(m.Id.Value), Service.GetWallpaper(m.Id), StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .ToList();

        Console.WriteLine(changed.Count == 0
            ? "other monitors: unchanged"
            : $"OTHER MONITORS CHANGED: {string.Join(", ", changed)}");
    }

    // A dark field with a 1-pixel magenta border and a 1-pixel checkerboard. Any resampling turns the
    // checkerboard to a flat gray and blurs the border, which is visible by eye and exact by compare.
    private static BitmapSource Pattern(int width, int height, string label, Color field)
    {
        var stride = width * 3;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var o = (y * stride) + (x * 3);
                var border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                var inChecker = x >= 140 && x < width - 140 && y >= 760 && y < 1160;
                Color c = border ? Colors.Magenta
                    : inChecker ? ((x + y) % 2 == 0 ? Colors.White : Colors.Black)
                    : field;
                pixels[o] = c.B;
                pixels[o + 1] = c.G;
                pixels[o + 2] = c.R;
            }
        }

        var raw = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(raw, new Rect(0, 0, width, height));
            dc.DrawText(Text(label, 96), new Point(140, 220));
            dc.DrawText(Text($"{width} x {height}", 48), new Point(140, 360));
            dc.DrawText(Text("Borda magenta de 1 px nos 4 lados", 32), new Point(140, 460));
            dc.DrawText(Text("Quadriculado abaixo: 1 px, sem cinza liso", 32), new Point(140, 510));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return new FormatConvertedBitmap(target, PixelFormats.Bgr24, null, 0);
    }

    private static FormattedText Text(string text, double size) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.White, null, TextFormattingMode.Display, 1.0);

    // Smooth colour with moderate grain, so PNG sizes resemble a photo more than a flat test card does.
    private static BitmapSource Photo(int width, int height, int seed)
    {
        var random = new Random(seed);
        var stride = width * 3;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double u = (double)x / width, v = (double)y / height;
                var grain = random.Next(-8, 9);
                var o = (y * stride) + (x * 3);
                pixels[o] = Clamp(90 + (90 * Math.Sin((u * 7) + seed)) + grain);
                pixels[o + 1] = Clamp(80 + (70 * Math.Cos((v * 5) + (u * 3))) + grain);
                pixels[o + 2] = Clamp(70 + (80 * Math.Sin((u + v) * 4)) + grain);
            }
        }

        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(value, 0, 255);

    private static void WritePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static BitmapSource Decode(string path)
    {
        using var file = File.OpenRead(path);
        var frame = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        return new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var stride = image.PixelWidth * 3;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static double Median(List<double> values) => Percentile(values, 0.5);

    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.Order().ToList();
        return sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(p * sorted.Count))];
    }
}
