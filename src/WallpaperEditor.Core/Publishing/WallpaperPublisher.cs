using System.Security.Cryptography;
using System.Text;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Publishing;

/// <summary>The current Windows setting would make a per-monitor wallpaper meaningless.</summary>
public sealed class SpanModeActiveException()
    : Exception("Windows is spanning one image across all monitors.");

/// <summary>There is nothing to go back to, or the file no longer exists.</summary>
public sealed class OriginalWallpaperUnavailableException(string? path)
    : Exception($"The original wallpaper is not available: {path ?? "(none)"}")
{
    public string? OriginalPath { get; } = path;
}

/// <summary>
/// Turns a chosen image into what one monitor displays, and remembers it. Any failure leaves the
/// monitor and the saved state as they were.
/// </summary>
public sealed class WallpaperPublisher(
    IMonitorService monitors,
    IWallpaperService wallpapers,
    IWallpaperRenderer renderer,
    AppStateStore store,
    string publishedRoot)
{
    /// <summary>
    /// Published files kept per monitor: the current one and the one before it. SetWallpaper returns
    /// before the shell has read the file, so during quick successive changes the previous file may
    /// still be in use. Keeping it costs one PNG on disk.
    /// </summary>
    private const int FilesKeptPerMonitor = 2;

    public Task<MonitorAssignment> PublishAsync(
        MonitorId monitorId,
        string sourcePath,
        FitMode fit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return PublishCoreAsync(
            monitorId,
            (size, output, cancel) => renderer.RenderToFileAsync(new StaticImageRequest(sourcePath, fit, size), output, cancel),
            assignment => assignment with
            {
                SourcePath = Path.GetFullPath(sourcePath),
                Fit = fit,
                ProjectPath = null,
                ProjectRevision = null,
            },
            cancellationToken);
    }

    /// <summary>Publishes one revision of a project. If the monitor has another shape, the whole canvas adapts.</summary>
    /// <param name="monitorId">The monitor to change.</param>
    /// <param name="projectPath">The project, remembered so the monitor knows what it is showing.</param>
    /// <param name="revision">The revision being published.</param>
    /// <param name="document">The document of that revision.</param>
    /// <param name="canvas">The project's canvas.</param>
    /// <param name="resolveAsset">Finds the project's asset files.</param>
    /// <param name="skipUnreadableAssets">Publish without an asset that cannot be read, at the user's request.</param>
    /// <param name="cancellationToken">Cancels before the wallpaper changes.</param>
    public Task<MonitorAssignment> PublishProjectAsync(
        MonitorId monitorId,
        string projectPath,
        int revision,
        WallpaperDocument document,
        PixelSize canvas,
        Func<string, string> resolveAsset,
        bool skipUnreadableAssets = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolveAsset);

        return PublishCoreAsync(
            monitorId,
            (size, output, cancel) => renderer.RenderDocumentToFileAsync(
                new DocumentRenderRequest(document, canvas, resolveAsset, size, FitMode.Fill, skipUnreadableAssets), output, cancel),
            assignment => assignment with
            {
                SourcePath = null,
                Fit = FitMode.Fill,
                ProjectPath = Path.GetFullPath(projectPath),
                ProjectRevision = revision,
            },
            cancellationToken);
    }

    /// <summary>The monitors currently showing a project, as far as this machine knows.</summary>
    public IReadOnlyList<MonitorAssignment> AssignmentsOf(string projectPath)
    {
        var full = Path.GetFullPath(projectPath);
        return store.Load().Monitors.Values
            .Where(a => IsSamePath(a.ProjectPath, full))
            .ToList();
    }

    public string? LastProjectPath => store.Load().LastProjectPath;

    public void RememberProject(string? projectPath)
    {
        var state = store.Load();
        state.LastProjectPath = projectPath;
        store.Save(state);
    }

    private async Task<MonitorAssignment> PublishCoreAsync(
        MonitorId monitorId,
        Func<PixelSize, string, CancellationToken, Task> render,
        Func<MonitorAssignment, MonitorAssignment> describe,
        CancellationToken cancellationToken)
    {
        var monitor = FindActive(monitorId);
        if (wallpapers.GetPosition() == WallpaperPosition.Span)
        {
            throw new SpanModeActiveException();
        }

        var folder = Directory.CreateDirectory(FolderFor(monitorId)).FullName;
        var temporary = Path.Combine(folder, $".tmp-{Guid.NewGuid():N}.png");
        var file = Path.Combine(folder, $"{Guid.NewGuid():N}.png");

        try
        {
            await render(monitor.Bounds.Size, temporary, cancellationToken);
            File.Move(temporary, file);

            var state = store.Load();
            var assignment = WithOriginalCaptured(state, monitor);

            // Saved before the change: if the app dies right after it, the original is not lost.
            state.Monitors[monitorId.Value] = assignment;
            store.Save(state);

            wallpapers.SetWallpaper(monitorId, file);

            assignment = describe(assignment) with
            {
                PublishedFile = file,
                PublishedSize = monitor.Bounds.Size,
                PublishedAt = DateTimeOffset.UtcNow,
            };
            state.Monitors[monitorId.Value] = assignment;
            store.Save(state);

            RemoveOldFiles(folder, file);
            return assignment;
        }
        catch
        {
            TryDelete(temporary);
            TryDelete(file);
            throw;
        }
    }

    /// <summary>Puts back what the monitor had before the app first touched it.</summary>
    public void RestoreOriginal(MonitorId monitorId)
    {
        ArgumentNullException.ThrowIfNull(monitorId);

        FindActive(monitorId);

        var state = store.Load();
        var original = state.Monitors.GetValueOrDefault(monitorId.Value)?.OriginalWallpaper;
        if (original is null || !File.Exists(original))
        {
            throw new OriginalWallpaperUnavailableException(original);
        }

        wallpapers.SetWallpaper(monitorId, original);

        state.Monitors.Remove(monitorId.Value);
        store.Save(state);

        // The shell may still be reading the last published file for a moment; a leftover is harmless
        // and is removed on the next publish.
        RemoveOldFiles(FolderFor(monitorId), keep: null);
    }

    public MonitorAssignment? GetAssignment(MonitorId monitorId) =>
        store.Load().Monitors.GetValueOrDefault(monitorId.Value);

    /// <summary>
    /// True if the monitor's resolution or orientation changed since the app rendered for it, and the
    /// image the app published is still the one on screen.
    /// </summary>
    public bool NeedsRepublish(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        return GetAssignment(monitor.Id) is { PublishedFile: { } file, PublishedSize: { } size }
            && size != monitor.Bounds.Size
            && IsSamePath(wallpapers.GetWallpaper(monitor.Id), file);
    }

    private MonitorInfo FindActive(MonitorId monitorId) =>
        monitors.GetMonitors().FirstOrDefault(m => m.Id == monitorId)
            ?? throw new MonitorUnavailableException(monitorId);

    /// <summary>
    /// The current wallpaper counts as the original the first time, and also whenever someone changed
    /// it outside the app: that newer choice is what "restore" should bring back.
    /// </summary>
    private MonitorAssignment WithOriginalCaptured(AppState state, MonitorInfo monitor)
    {
        var current = wallpapers.GetWallpaper(monitor.Id);
        var existing = state.Monitors.GetValueOrDefault(monitor.Id.Value);

        if (existing is null)
        {
            return new MonitorAssignment
            {
                MonitorId = monitor.Id.Value,
                LastKnownName = monitor.Name,
                OriginalWallpaper = current,
            };
        }

        return IsSamePath(current, existing.PublishedFile)
            ? existing with { LastKnownName = monitor.Name }
            : existing with { LastKnownName = monitor.Name, OriginalWallpaper = current };
    }

    private string FolderFor(MonitorId monitorId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(monitorId.Value.ToUpperInvariant()));
        return Path.Combine(publishedRoot, Convert.ToHexStringLower(hash)[..16]);
    }

    private static void RemoveOldFiles(string folder, string? keep)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var files = new DirectoryInfo(folder).GetFiles("*.png")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        var obsolete = keep is null
            ? files
            : files.Where(f => !IsSamePath(f.FullName, keep)).Skip(FilesKeptPerMonitor - 1);

        foreach (var file in obsolete)
        {
            TryDelete(file.FullName);
        }
    }

    private static bool IsSamePath(string? left, string? right) =>
        left is not null && right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>A file that cannot be deleted now only costs disk, and goes away on a later publish.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
