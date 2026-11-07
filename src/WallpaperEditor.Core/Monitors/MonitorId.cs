namespace WallpaperEditor.Core.Monitors;

/// <summary>
/// Identifies one monitor by the device path Windows reports for it.
/// </summary>
/// <remarks>
/// A null monitor id means "every monitor" to <c>IDesktopWallpaper::SetWallpaper</c>, which is the
/// exact thing this app exists to avoid. Making an empty id unrepresentable keeps that call out of
/// reach by construction.
/// </remarks>
public sealed record MonitorId
{
    public MonitorId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
