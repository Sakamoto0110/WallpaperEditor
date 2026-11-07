using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Rendering;

/// <summary>An image file whose size and orientation are known, without its pixels decoded yet.</summary>
internal sealed class LoadedImage
{
    private readonly string _path;
    private readonly int _rawWidth;
    private readonly int _rawHeight;
    private readonly ushort _orientation;

    private LoadedImage(string path, int rawWidth, int rawHeight, ushort orientation)
    {
        _path = path;
        _rawWidth = rawWidth;
        _rawHeight = rawHeight;
        _orientation = orientation;
    }

    /// <summary>EXIF orientations 5 to 8 turn the image by a quarter, exchanging width and height.</summary>
    private bool SwapsAxes => _orientation is >= 5 and <= 8;

    /// <summary>The size as a person sees it, which for a phone photo may differ from the stored one.</summary>
    public PixelSize DisplaySize => SwapsAxes ? new(_rawHeight, _rawWidth) : new(_rawWidth, _rawHeight);

    public static LoadedImage Open(string path)
    {
        return Guard(path, () =>
        {
            using var file = OpenFile(path);
            var frame = BitmapDecoder.Create(file, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return new LoadedImage(path, frame.PixelWidth, frame.PixelHeight, ReadOrientation(frame));
        });
    }

    /// <summary>
    /// Decodes only as many pixels as the placement needs. A 24-megapixel photo for a small monitor would
    /// otherwise spend about 100 MiB of memory on detail that gets thrown away.
    /// </summary>
    public BitmapSource Decode(Placement placement)
    {
        return Guard(_path, () =>
        {
            var neededRawWidth = SwapsAxes ? placement.Height : placement.Width;
            var neededRawHeight = SwapsAxes ? placement.Width : placement.Height;
            var scale = Math.Max(neededRawWidth / _rawWidth, neededRawHeight / _rawHeight);

            using var file = OpenFile(_path);
            var image = new BitmapImage();
            image.BeginInit();

            // A stream, not a URI: WPF caches images by URI, and a file can change under the same name.
            // BitmapCreateOptions.IgnoreImageCache must stay off, as it throws when there is no URI.
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = file;
            if (scale < 1)
            {
                image.DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(_rawWidth * scale));
            }

            image.EndInit();
            image.Freeze();

            return ApplyOrientation(image);
        });
    }

    private static FileStream OpenFile(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

    private static ushort ReadOrientation(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata metadata
                && metadata.GetQuery("System.Photo.Orientation") is ushort value
                && value is >= 1 and <= 8
                    ? value
                    : (ushort)1;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException or COMException)
        {
            // Formats without metadata support (BMP, for one) simply have no orientation.
            return 1;
        }
    }

    /// <summary>WPF ignores the EXIF orientation, so phone photos would otherwise appear turned.</summary>
    private BitmapSource ApplyOrientation(BitmapSource image)
    {
        Transform? transform = _orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Combined(new ScaleTransform(-1, 1), new RotateTransform(270)),
            6 => new RotateTransform(90),
            7 => Combined(new ScaleTransform(-1, 1), new RotateTransform(90)),
            8 => new RotateTransform(270),
            _ => null,
        };

        if (transform is null)
        {
            return image;
        }

        var turned = new TransformedBitmap(image, transform);
        turned.Freeze();
        return turned;
    }

    private static TransformGroup Combined(Transform first, Transform second)
    {
        var group = new TransformGroup();
        group.Children.Add(first);
        group.Children.Add(second);
        return group;
    }

    private static T Guard<T>(string path, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or FileFormatException or ArgumentException or InvalidOperationException or COMException or OverflowException)
        {
            throw new ImageLoadException(path, ex);
        }
    }
}
