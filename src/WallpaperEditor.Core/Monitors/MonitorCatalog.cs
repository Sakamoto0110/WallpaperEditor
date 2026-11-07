namespace WallpaperEditor.Core.Monitors;

/// <summary>An entry as enumerated by the OS, before deciding whether it is a real monitor.</summary>
/// <param name="Id">The device path.</param>
/// <param name="Bounds">The screen rectangle, or null if the OS could not provide one.</param>
public sealed record MonitorCandidate(MonitorId Id, PixelRect? Bounds);

public static class MonitorCatalog
{
    /// <summary>
    /// Turns raw enumeration results into the monitors a user can actually pick.
    /// </summary>
    /// <remarks>
    /// <c>GetMonitorDevicePathCount</c> also counts entries that are not attached (measured: 5 entries
    /// for 3 physical monitors), and <c>GetWallpaper</c> answers normally for them. Only the rectangle
    /// tells them apart. The result is ordered left to right because the enumeration order has no
    /// relation to where the monitors sit.
    /// </remarks>
    public static IReadOnlyList<MonitorInfo> Build(
        IEnumerable<MonitorCandidate> candidates,
        IReadOnlyDictionary<string, string> friendlyNames)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(friendlyNames);

        var names = new Dictionary<string, string>(friendlyNames, StringComparer.OrdinalIgnoreCase);

        return candidates
            .Where(c => c.Bounds is { IsEmpty: false })
            .Select(c => new MonitorInfo(c.Id, NameFor(c.Id, names), c.Bounds!.Value))
            .OrderBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .ToList();
    }

    private static string NameFor(MonitorId id, Dictionary<string, string> names) =>
        names.TryGetValue(id.Value, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : FallbackName(id);

    /// <summary>Extracts the model code from a path such as <c>\\?\DISPLAY#SAM093E#...</c>.</summary>
    private static string FallbackName(MonitorId id)
    {
        var parts = id.Value.Split('#');
        return parts.Length > 1 ? parts[1] : id.Value;
    }
}
