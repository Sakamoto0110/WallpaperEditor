namespace WallpaperEditor.Core.Monitors;

/// <summary>A size in physical pixels.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}
