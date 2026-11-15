using System.Text.Json;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Publishing;

/// <summary>What this machine remembers: which wallpaper the app put on which monitor.</summary>
public sealed class AppState
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Keyed by monitor device path. Kept for disconnected monitors too.</summary>
    public Dictionary<string, MonitorAssignment> Monitors { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opened again at startup: the usual reason to open the app is a small change to it.</summary>
    public string? LastProjectPath { get; set; }
}

public sealed record MonitorAssignment
{
    public required string MonitorId { get; init; }

    public string? LastKnownName { get; init; }

    /// <summary>The wallpaper the monitor had before the app first touched it.</summary>
    public string? OriginalWallpaper { get; init; }

    /// <summary>
    /// A copy of that file, kept in the app's own folder, so restoring still works if the original is moved or
    /// deleted. Null if it could not be copied, or for a state saved before the app kept copies.
    /// </summary>
    public string? OriginalCopy { get; init; }

    /// <summary>The image the user chose, for a static wallpaper. Null for a project.</summary>
    public string? SourcePath { get; init; }

    public FitMode Fit { get; init; }

    /// <summary>The project folder, for a dynamic wallpaper. Null for a static one.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>The revision of that project now on the monitor.</summary>
    public int? ProjectRevision { get; init; }

    /// <summary>The rendered file handed to Windows.</summary>
    public string? PublishedFile { get; init; }

    /// <summary>The size it was rendered at, to notice when the monitor's resolution changes.</summary>
    public PixelSize? PublishedSize { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>Reads and writes <c>state.json</c>.</summary>
public sealed class AppStateStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Returns an empty state rather than failing if the file cannot be used: a damaged state must not
    /// stop the app from opening. The unreadable file is set aside so nothing is silently overwritten.
    /// </summary>
    public AppState Load()
    {
        if (!File.Exists(path))
        {
            return new AppState();
        }

        try
        {
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), Options);
            if (state is { FormatVersion: AppState.CurrentFormatVersion })
            {
                return new AppState
                {
                    Monitors = new(state.Monitors, StringComparer.OrdinalIgnoreCase),
                    LastProjectPath = state.LastProjectPath,
                };
            }
        }
        catch (JsonException)
        {
            // Falls through to setting the file aside.
        }

        File.Move(path, $"{path}.{DateTime.UtcNow:yyyyMMddHHmmss}.unreadable", overwrite: true);
        return new AppState();
    }

    public void Save(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Unique, since the companion process may save at the same moment as the app.
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
