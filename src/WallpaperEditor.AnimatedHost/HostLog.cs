using System.Globalization;

namespace WallpaperEditor.AnimatedHost;

/// <summary>A line-by-line log, flushed at once: the host has no window, and may be killed.</summary>
internal static class HostLog
{
    private static readonly object Gate = new();
    private static string? path;

    public static void Open(string? file)
    {
        path = file;
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {message}";
        lock (Gate)
        {
            Console.Out.WriteLine(line);
            if (path is not null)
            {
                File.AppendAllLines(path, [line]);
            }
        }
    }
}
