using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace WallpaperEditor.Platform.Windows;

/// <summary>Reads the names monitors report about themselves, which IDesktopWallpaper does not expose.</summary>
public static unsafe class DisplayNames
{
    /// <summary>Maps each active monitor's device path to its friendly name.</summary>
    /// <remarks>
    /// Names are cosmetic, so failure here yields an empty map rather than an error: the app can still
    /// list and use monitors under their model codes.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Read()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in QueryActivePaths())
        {
            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
            target.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            target.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
            target.header.adapterId = path.targetInfo.adapterId;
            target.header.id = path.targetInfo.id;

            if (PInvoke.DisplayConfigGetDeviceInfo(ref target.header) == 0)
            {
                names[new string(target.monitorDevicePath.Value)] = new string(target.monitorFriendlyDeviceName.Value);
            }
        }

        return names;
    }

    private static DISPLAYCONFIG_PATH_INFO[] QueryActivePaths()
    {
        const QUERY_DISPLAY_CONFIG_FLAGS Flags = QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS;

        // The configuration can change between asking for the sizes and reading it.
        while (true)
        {
            if (PInvoke.GetDisplayConfigBufferSizes(Flags, out var pathCount, out var modeCount) != WIN32_ERROR.ERROR_SUCCESS)
            {
                return [];
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            switch (PInvoke.QueryDisplayConfig(Flags, ref pathCount, paths, ref modeCount, modes))
            {
                case WIN32_ERROR.ERROR_SUCCESS:
                    return paths[..(int)pathCount];
                case WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER:
                    continue;
                default:
                    return [];
            }
        }
    }
}
