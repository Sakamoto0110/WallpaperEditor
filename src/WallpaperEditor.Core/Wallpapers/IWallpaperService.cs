using WallpaperEditor.Core.Monitors;

namespace WallpaperEditor.Core.Wallpapers;

public interface IWallpaperService
{
    /// <summary>The image path Windows has for this monitor, or null if it has none.</summary>
    string? GetWallpaper(MonitorId monitor);

    /// <summary>Sets the image for one monitor, leaving the others untouched.</summary>
    /// <exception cref="MonitorUnavailableException">The monitor is not currently attached.</exception>
    /// <exception cref="FileNotFoundException">The image does not exist.</exception>
    void SetWallpaper(MonitorId monitor, string imagePath);

    /// <summary>The fit mode, which Windows applies to all monitors at once.</summary>
    WallpaperPosition GetPosition();

    SlideshowState GetSlideshowState();
}

/// <summary>Values match <c>DESKTOP_WALLPAPER_POSITION</c>.</summary>
public enum WallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

/// <summary>Values match <c>DESKTOP_SLIDESHOW_STATE</c>.</summary>
[Flags]
public enum SlideshowState
{
    None = 0,
    Enabled = 1,
    Slideshow = 2,
    DisabledByRemoteSession = 4,
}

public sealed class MonitorUnavailableException(MonitorId monitor)
    : Exception($"The monitor is not attached: {monitor}")
{
    public MonitorId Monitor { get; } = monitor;
}
