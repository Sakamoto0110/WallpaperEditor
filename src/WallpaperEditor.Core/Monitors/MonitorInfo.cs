namespace WallpaperEditor.Core.Monitors;

/// <summary>A physical monitor that is currently active.</summary>
public sealed record MonitorInfo(MonitorId Id, string Name, PixelRect Bounds)
{
    /// <summary>Windows places the primary monitor at the origin of the virtual desktop.</summary>
    public bool IsPrimary => Bounds is { Left: 0, Top: 0 };

    public bool IsPortrait => Bounds.IsPortrait;
}
