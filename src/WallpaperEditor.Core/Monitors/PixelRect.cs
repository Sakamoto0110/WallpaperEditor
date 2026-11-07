namespace WallpaperEditor.Core.Monitors;

/// <summary>
/// A rectangle in physical screen pixels. Coordinates may be negative: the primary monitor sits at
/// the origin, and monitors to its left or above it have negative coordinates.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool IsPortrait => Height > Width;

    public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
}
