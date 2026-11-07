using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Rendering;

/// <summary>An image adapted to an exact pixel size.</summary>
public sealed record StaticImageRequest(string SourcePath, FitMode Fit, PixelSize Target);

/// <summary>A document drawn on its canvas, and the canvas adapted to an exact pixel size.</summary>
/// <param name="Document">What to draw.</param>
/// <param name="Canvas">The coordinate system of the document's items.</param>
/// <param name="ResolveAsset">Turns an asset name into a file path.</param>
/// <param name="Target">The size of the result.</param>
/// <param name="Fit">How the canvas adapts when the target has another shape.</param>
/// <param name="SkipUnreadableAssets">Draw what can be drawn, instead of failing on an unreadable asset.</param>
public sealed record DocumentRenderRequest(
    WallpaperDocument Document,
    PixelSize Canvas,
    Func<string, string> ResolveAsset,
    PixelSize Target,
    FitMode Fit = FitMode.Fill,
    bool SkipUnreadableAssets = false);

public interface IWallpaperRenderer
{
    /// <summary>Writes an opaque 8-bit PNG with exactly the requested size.</summary>
    /// <exception cref="ImageLoadException">The source is missing or is not a readable image.</exception>
    Task RenderToFileAsync(StaticImageRequest request, string outputPath, CancellationToken cancellationToken = default);

    /// <summary>Writes an opaque 8-bit PNG with exactly the requested size.</summary>
    /// <exception cref="ImageLoadException">An asset is missing or unreadable, and skipping was not requested.</exception>
    Task RenderDocumentToFileAsync(DocumentRenderRequest request, string outputPath, CancellationToken cancellationToken = default);
}

public sealed class ImageLoadException(string path, Exception? inner = null)
    : Exception($"Could not read the image: {path}", inner)
{
    public string ImagePath { get; } = path;
}
