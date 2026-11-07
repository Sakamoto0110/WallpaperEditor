using System.Windows.Media.Imaging;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Rendering;

/// <summary>
/// Decoded asset images, kept per asset at the largest size asked for so far. Belongs to one thread, though
/// the bitmaps it hands out are frozen.
/// </summary>
public sealed class DocumentImages(Func<string, string> resolveAsset)
{
    private readonly Dictionary<string, LoadedImage?> _files = [];
    private readonly Dictionary<string, BitmapSource> _decoded = [];
    private readonly HashSet<string> _unreadable = [];

    /// <summary>Assets that could not be read so far, by the name they were imported with.</summary>
    public IReadOnlyCollection<string> Unreadable => _unreadable;

    /// <summary>The size of an asset as a person sees it, or null if it cannot be read.</summary>
    public PixelSize? SizeOf(AssetRef asset) => Open(asset)?.DisplaySize;

    /// <summary>The image at no less than the given size, when the file has that many pixels.</summary>
    public BitmapSource? Get(AssetRef asset, double width, double height)
    {
        if (Open(asset) is not { } file)
        {
            return null;
        }

        if (_decoded.TryGetValue(asset.Asset, out var existing) && IsEnough(existing, file, width, height))
        {
            return existing;
        }

        try
        {
            var bitmap = file.Decode(new Placement(0, 0, width, height));
            _decoded[asset.Asset] = bitmap;
            return bitmap;
        }
        catch (ImageLoadException)
        {
            _unreadable.Add(asset.OriginalName);
            return null;
        }
    }

    /// <summary>Whatever is already decoded, without decoding: keeps a drag smooth while resizing.</summary>
    public BitmapSource? GetCached(AssetRef asset) => _decoded.GetValueOrDefault(asset.Asset);

    private LoadedImage? Open(AssetRef asset)
    {
        if (_files.TryGetValue(asset.Asset, out var known))
        {
            return known;
        }

        LoadedImage? file = null;
        try
        {
            file = LoadedImage.Open(resolveAsset(asset.Asset));
        }
        catch (Exception ex) when (ex is ImageLoadException or ProjectUnavailableException)
        {
            _unreadable.Add(asset.OriginalName);
        }

        _files[asset.Asset] = file;
        return file;
    }

    private static bool IsEnough(BitmapSource bitmap, LoadedImage file, double width, double height)
    {
        var isFullSize = bitmap.PixelWidth >= file.DisplaySize.Width && bitmap.PixelHeight >= file.DisplaySize.Height;
        return isFullSize || (bitmap.PixelWidth >= Math.Ceiling(width) && bitmap.PixelHeight >= Math.Ceiling(height));
    }
}
