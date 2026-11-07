namespace WallpaperEditor.Core.Monitors;

public interface IMonitorService
{
    /// <summary>The active physical monitors, ordered left to right.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();
}
