using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Platform.Windows;

/// <summary>Monitors and wallpapers through <c>IDesktopWallpaper</c>.</summary>
/// <remarks>The process must be per-monitor DPI aware, or the rectangles come back virtualized.</remarks>
public sealed unsafe class DesktopWallpaperService : IMonitorService, IWallpaperService
{
    public IReadOnlyList<MonitorInfo> GetMonitors() =>
        MonitorCatalog.Build(GetCandidates(), DisplayNames.Read());

    /// <summary>Everything the OS enumerates, including entries that are not attached.</summary>
    public IReadOnlyList<MonitorCandidate> GetCandidates()
    {
        var api = Create();
        api.GetMonitorDevicePathCount(out var count);

        var candidates = new List<MonitorCandidate>((int)count);
        for (uint i = 0; i < count; i++)
        {
            PWSTR path;
            api.GetMonitorDevicePathAt(i, &path);
            var id = new MonitorId(TakeString(path));
            candidates.Add(new MonitorCandidate(id, ReadBounds(api, id)));
        }

        return candidates;
    }

    public string? GetWallpaper(MonitorId monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var api = Create();
        PWSTR path;
        fixed (char* id = monitor.Value)
        {
            api.GetWallpaper(id, &path);
        }

        var result = TakeString(path);
        return result.Length == 0 ? null : result;
    }

    public void SetWallpaper(MonitorId monitor, string imagePath)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The wallpaper image does not exist.", fullPath);
        }

        var api = Create();
        if (ReadBounds(api, monitor) is null)
        {
            throw new MonitorUnavailableException(monitor);
        }

        fixed (char* id = monitor.Value, file = fullPath)
        {
            api.SetWallpaper(id, file);
        }
    }

    public WallpaperPosition GetPosition()
    {
        DESKTOP_WALLPAPER_POSITION position;
        Create().GetPosition(&position);
        return (WallpaperPosition)position;
    }

    public void SetPosition(WallpaperPosition position) =>
        Create().SetPosition((DESKTOP_WALLPAPER_POSITION)position);

    public SlideshowState GetSlideshowState()
    {
        DESKTOP_SLIDESHOW_STATE state;
        Create().GetStatus(&state);
        return (SlideshowState)state;
    }

    /// <summary>
    /// Returns null for anything but <c>S_OK</c>: a detached monitor is documented to give
    /// <c>S_FALSE</c>, a success code, but was measured failing with an error as well.
    /// </summary>
    private static PixelRect? ReadBounds(IDesktopWallpaper api, MonitorId monitor)
    {
        RECT rect;
        fixed (char* id = monitor.Value)
        {
            if (api.GetMonitorRECT(id, &rect) != HRESULT.S_OK)
            {
                return null;
            }
        }

        return new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
    }

    private static IDesktopWallpaper Create() => (IDesktopWallpaper)new DesktopWallpaper();

    /// <summary>Copies a string the COM server allocated, and frees it.</summary>
    private static string TakeString(PWSTR value)
    {
        try
        {
            return Marshal.PtrToStringUni((nint)value.Value) ?? string.Empty;
        }
        finally
        {
            PInvoke.CoTaskMemFree(value.Value);
        }
    }
}
